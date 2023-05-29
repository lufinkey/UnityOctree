using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Octrees
{
	public struct ItemInfoWithDistance<T>
	{
		public float distance;
		public Vector3 position;
		public T obj;
	}
	
	// A node in a PointOctree
	// Copyright 2014 Nition, BSD licence (see LICENCE file). www.momentstudio.co.nz
	public class PointOctreeNode<T> {
		// If there are already MaxNodeEntries in a node, we split it into children
		// A generally good number seems to be something around 8-15
		const int MaxNodeEntries = 8;
		
		private struct BoxInfo {
			public Vector3 center;
			public float length;
			public Bounds bounds;
			
			public BoxInfo(Vector3 center, float length) {
				this.center = center;
				this.length = length;
				this.bounds = new Bounds(center, new Vector3(length, length, length));
			}

			public bool Encapsulates(in Vector3 point) {
				return bounds.Contains(point);
			}
		}
		
		public readonly PointOctree<T> tree;
		
		// Size/positioning info for this node
		private BoxInfo _boxInfo;
		
		// the object entries for this node
		private Dictionary<T,Vector3> _entries = new Dictionary<T,Vector3>();
		// The child entries for this node
		private Dictionary<T,OctreeNodeSector> _childEntries;
		// The subdivided nodes within this node
		private PointOctreeNode<T>[] _childNodes;
		// Size/positioning info of potential children to this node
		private BoxInfo[] _childBoxes;

		/// <summary>
		/// Constructs a new point octree node
		/// </summary>
		/// <param name="tree">The tree that this node belongs to</param>
		/// <param name="center">The center position of this node</param>
		/// <param name="size">The length of a side this node, not taking looseness into account.</param>
		public PointOctreeNode(PointOctree<T> tree, Vector3 center, float size) {
			this.tree = tree;
			SetValues(center, size);
		}
		
		
		#region Properties
		/// <summary>
		/// The center point of this node
		/// </summary>
		public Vector3 center => _boxInfo.center;
		
		/// <summary>
		/// The side length of this node
		/// </summary>
		public float baseLength => _boxInfo.length;
		
		/// <summary>
		/// The full bounding box of this node
		/// </summary>
		public Bounds bounds => _boxInfo.bounds;
		
		/// <summary>
		/// The objects contained within only this node (not any of its children)
		/// </summary>
		public IReadOnlyCollection<T> objectsInSelf => _entries.Keys;
		
		/// <summary>
		/// The objects contained in the children of this node
		/// </summary>
		public IReadOnlyCollection<T> objectsInChildren => _childEntries?.Keys ?? (IReadOnlyCollection<T>)System.Array.Empty<T>();
		
		/// <summary>
		/// The child nodes of this node. This list or some of its entries may be null
		/// </summary>
		public IReadOnlyList<PointOctreeNode<T>> childNodes => _childNodes;
		
		/// <summary>
		/// The combined number of objects contained in this node and its children
		/// </summary>
		public int Count => (_entries.Count + (_childEntries?.Count ?? 0));
		
		/// <summary>
		/// Checks if this node or anything below it has something in it.
		/// </summary>
		/// <returns>True if this node or any of its children, grandchildren etc have something in them</returns>
		public bool HasAnyObjects() => (_entries.Count > 0 || (_childEntries != null && _childEntries.Count > 0));
		#endregion
		
		
		
		#region Add / Remove / Move / Access Entries
		/// <summary>
		/// Tells if this node contains the given object in itself or its children
		/// </summary>
		/// <param name="obj">The object to check</param>
		/// <returns>true if the node or its children contains the object, false otherwise</returns>
		public bool Contains(T obj) {
			return _entries.ContainsKey(obj) || (_childEntries?.ContainsKey(obj) ?? false);
		}
		
		/// <summary>
		/// Attempts to get the position of an entry in this node or its children
		/// </summary>
		/// <param name="obj">The entry to get the bounds for</param>
		/// <param name="objPosition">The position of the object entry</param>
		/// <returns>true if the object was found in the octree node or its children, false if it was not</returns>
		public bool TryGetPosition(T obj, out Vector3 objPosition) {
			if (_entries.TryGetValue(obj, out objPosition)) {
				return true;
			} else if (_childEntries != null && _childEntries.TryGetValue(obj, out var entrySector)) {
				return _childNodes[(int)entrySector].TryGetPosition(obj, out objPosition);
			}
			objPosition = default;
			return false;
		}
		
		/// <summary>
		/// Gets all the objects in this node
		/// </summary>
		/// <returns>An enumerable of all the objects in this node</returns>
		public IEnumerable<T> GetAll() {
			if (_childEntries == null) {
				return _entries.Keys;
			}
			return _entries.Keys.Concat(_childEntries.Keys);
		}
		
		/// <summary>
		/// Add an object.
		/// </summary>
		/// <param name="obj">Object to add.</param>
		/// <param name="objPosition">Position of the object.</param>
		/// <returns></returns>
		public bool Add(T obj, in Vector3 objPosition) {
			if (!_boxInfo.Encapsulates(objPosition)) {
				return false;
			}
			if (Remove(obj, isRoot:false, mergeIfAble:false)) {
				Debug.LogWarning("Calling Add when obj is already contained within node");
			}
			NoCheckAdd(obj, objPosition);
			return true;
		}
		
		/// <summary>
		/// Add or set an entry in this node or its children without checking current added state or encapsulation
		/// </summary>
		/// <param name="obj">The object to add</param>
		/// <param name="objPosition">The position of the object</param>
		private void NoCheckAdd(T obj, in Vector3 objPosition) {
			// We always put things in the deepest possible child
			// So we can skip checks and simply move down if there are children aleady
			if (_childNodes == null) {
				// Just add if few objects are here, or children would be below min size
				if (_entries.Count < MaxNodeEntries || (_boxInfo.length / 2.0f) < tree.minNodeSize) {
					_entries[obj] = objPosition;
					return; // We're done. No children yet
				}
				Split();
			}
			// Find which child node the object should reside in
			var childNode = GetBestFitChildNode(objPosition, out var childSector);
			childNode.NoCheckAdd(obj, objPosition);
			_childEntries[obj] = childSector;
		}
	
		/// <summary>
		/// Remove an object. Makes the assumption that the object only exists once in the tree.
		/// </summary>
		/// <param name="obj">Object to remove.</param>
		/// <param name="isRoot">Specifies if this node is the root node. The root node won't be merged even if <paramref name="mergeIfAble"/> is true</param>
		/// <param name="mergeIfAble">Specifies whether child nodes should get merged if they're able to be</param>
		/// <returns>True if the object was removed successfully.</returns>
		public bool Remove(T obj, bool isRoot, bool mergeIfAble = true) {
			bool removed = false;
			// try to remove object from self
			if (_entries.Remove(obj)) {
				removed = true;
			}
			// try to remove object from children
			else if (_childEntries != null && _childEntries.TryGetValue(obj, out var childSector)) {
				var childNode = _childNodes[(int)childSector];
				removed = childNode.Remove(obj, isRoot:false, mergeIfAble:mergeIfAble);
				_childEntries.Remove(obj);
			}
			// If we're removed and we have children, check if we should merge nodes now that we've removed an item
			if (removed && mergeIfAble && !isRoot && _childNodes != null && ShouldMerge()) {
				Merge();
			}
			return removed;
		}
		#endregion
		
		
		
		#region Encapsulation
		private PointOctreeNode<T> GetBestFitChildNode(Vector3 point, out OctreeNodeSector childSector) {
			childSector = OctreeUtils.GetSector(point - _boxInfo.center);
			int childIndex = (int)childSector;
			var childBoxInfo = _childBoxes[childIndex];
			var childNode = _childNodes[childIndex];
			if (childNode == null) {
				childNode = new PointOctreeNode<T>(tree, childBoxInfo.center, childBoxInfo.length);
				_childNodes[childIndex] = childNode;
			}
			return childNode;
		}
		#endregion
		
		
		
		#region Intersection / Search
		/// <summary>
		/// Return objects that are within maxDistance of the specified ray.
		/// </summary>
		/// <param name="ray">The ray to compare distance to</param>
		/// <param name="maxDistance">Maximum distance from the ray to consider.</param>
		/// <param name="results">A list of results to populate</param>
		/// <param name="filter">Filter objects to include (return true to include the object, false to not include it)</param>
		/// <returns>The number of objects in this node or its children that are within the max distance to the ray</returns>
		public int GetNearby(in Ray ray, float maxDistance, ref List<T> results, PointOctree<T>.EntryFilter filter) {
			// Does the ray hit this node at all?
			// Note: Expanding the bounds is not exactly the same as a real distance check, but it's fast.
			// TODO: Does someone have a fast AND accurate formula to do this check?
			var adjustedBounds = _boxInfo.bounds;
			float doubleMaxDistance = maxDistance * 2.0f;
			adjustedBounds.Expand(new Vector3(doubleMaxDistance, doubleMaxDistance, doubleMaxDistance));
			// ensure ray intersects the expanded bounds
			if (!adjustedBounds.IntersectRay(ray)) {
				return 0;
			}
			int totalNearby = 0;
			// Check against any entries in this node
			float sqrMaxDistance = maxDistance * maxDistance;
			if (filter == null) {
				foreach (var (obj, objPosition) in _entries) {
					if (OctreeUtils.SqrDistanceToRay(ray, objPosition) <= sqrMaxDistance) {
						if (results == null) {
							results = new List<T>();
						}
						results.Add(obj);
						totalNearby++;
					}
				}
			} else {
				foreach (var (obj, objPosition) in _entries) {
					if (!filter(obj, objPosition)) {
						continue;
					}
					if (OctreeUtils.SqrDistanceToRay(ray, objPosition) <= sqrMaxDistance) {
						if (results == null) {
							results = new List<T>();
						}
						results.Add(obj);
						totalNearby++;
					}
				}
			}
			// Check children
			if (_childNodes != null) {
				foreach (var childNode in _childNodes) {
					if (childNode != null) {
						totalNearby += childNode.GetNearby(ray, maxDistance, ref results, filter);
					}
				}
			}
			return totalNearby;
		}
		
		/// <summary>
		/// Checks if the given sphere intersects with this node
		/// </summary>
		/// <param name="sphereCenter">The center position of the sphere</param>
		/// <param name="sphereRadius">The radius of the sphere</param>
		/// <returns>true if the sphere intersects, false if it does not</returns>
		public bool IsSphereIntersectingNode(Vector3 sphereCenter, float sphereRadius) {
			#if UNITY_2017_1_OR_NEWER
			// Does the node intersect with the given sphere
			float sqrDistanceToSphereCenter = (_boxInfo.bounds.ClosestPoint(sphereCenter) - sphereCenter).sqrMagnitude;
			return (sqrDistanceToSphereCenter <= (sphereRadius * sphereRadius));
			#else
			// Does the ray hit this node at all?
			// Note: Expanding the bounds is not exactly the same as a real distance check, but it's fast
			// TODO: Does someone have a fast AND accurate formula to do this check?
			var adjustedBounds = _boxInfo.bounds;
			float doubleMaxDistance = maxDistance * 2.0f;
			adjustedBounds.Expand(new Vector3(doubleMaxDistance, doubleMaxDistance, doubleMaxDistance));
			return adjustedBounds.Contains(position);
			#endif
		}
		
		/// <summary>
		/// Gets objects that are within <paramref name="maxDistance"/> of the specified position.
		/// </summary>
		/// <param name="position">The position to compare objects against</param>
		/// <param name="maxDistance">Maximum distance from the position to consider</param>
		/// <param name="results">The resulting list of nearby objects</param>
		/// <param name="filter">Filter objects to include (return true to include the object, false to not include it)</param>
		/// <returns>The number of objects in this node or its children that are within the max distance to the given position</returns>
		public int GetNearby(in Vector3 position, float maxDistance, ref List<T> results, PointOctree<T>.EntryFilter filter = null) {
			if (!IsSphereIntersectingNode(position, maxDistance)) {
				return 0;
			}
			float sqrMaxDistance = maxDistance * maxDistance;
			int totalNearby = 0;
			// Check against any objects in this node
			if (filter == null) {
				foreach (var (obj, objPosition) in _entries) {
					if ((position - objPosition).sqrMagnitude <= sqrMaxDistance) {
						if (results == null) {
							results = new List<T>();
						}
						results.Add(obj);
						totalNearby++;
					}
				}
			} else {
				foreach (var (obj, objPosition) in _entries) {
					if (!filter(obj, objPosition)) {
						continue;
					}
					if ((position - objPosition).sqrMagnitude <= sqrMaxDistance) {
						if (results == null) {
							results = new List<T>();
						}
						results.Add(obj);
						totalNearby++;
					}
				}
			}
			// Check children
			if (_childNodes != null) {
				foreach (var childNode in _childNodes) {
					if (childNode != null) {
						totalNearby += childNode.GetNearby(position, maxDistance, ref results, filter);
					}
				}
			}
			return totalNearby;
		}
		
		/// <summary>
		/// Gets objects that are within <paramref name="maxDistance"/> of the specified position, including their square distances in the results
		/// </summary>
		/// <param name="position">The position to compare objects against</param>
		/// <param name="maxDistance">Maximum distance from the position to consider</param>
		/// <param name="results">The resulting list of nearby objects, including their positions and square distances</param>
		/// <param name="filter">Filter objects to include (return true to include the object, false to not include it)</param>
		/// <returns>The number of objects in this node or its children that are within the max distance to the given position</returns>
		public int GetNearbyWithDistances(in Vector3 position, float maxDistance, ref List<PointOctree<T>.NearbyEntry> results, PointOctree<T>.EntryFilter filter = null) {
			if (!IsSphereIntersectingNode(position, maxDistance)) {
				return 0;
			}
			float sqrMaxDistance = maxDistance * maxDistance;
			int totalNearby = 0;
			// Check against any objects in this node
			if (filter == null) {
				foreach (var (obj, objPosition) in _entries) {
					float sqrObjDistance = (objPosition - position).sqrMagnitude;
					if (sqrObjDistance <= sqrMaxDistance) {
						if (results == null) {
							results = new List<PointOctree<T>.NearbyEntry>();
						}
						results.Add(new PointOctree<T>.NearbyEntry(obj, objPosition, sqrObjDistance));
						totalNearby++;
					}
				}
			} else {
				foreach (var (obj, objPosition) in _entries) {
					if (!filter(obj, objPosition)) {
						continue;
					}
					float sqrObjDistance = (objPosition - position).sqrMagnitude;
					if (sqrObjDistance <= sqrMaxDistance) {
						if (results == null) {
							results = new List<PointOctree<T>.NearbyEntry>();
						}
						results.Add(new PointOctree<T>.NearbyEntry(obj, objPosition, sqrObjDistance));
						totalNearby++;
					}
				}
			}
			// Check children
			if (_childNodes != null) {
				foreach (var childNode in _childNodes) {
					if (childNode != null) {
						totalNearby += childNode.GetNearbyWithDistances(position, maxDistance, ref results, filter);
					}
				}
			}
			return totalNearby;
		}
		
		/// <summary>
		/// Finds the best matching object in this node or its children, using a given fitness calculator and node filter
		/// </summary>
		/// <param name="fitnessCalculator">Calculates the "fitness" of entries (how similar it is to the desired result). Lower values are ranked as more "fit".</param>
		/// <param name="nodeFilter">Determines if a node should be searched</param>
		/// <param name="matchingObj">The closest match found in the Octree, or `default` if not found</param>
		/// <param name="matchFitness">The calculated "fitness" value of the matching object</param>
		/// <param name="filter">An optional filter method to only consider certain objects</param>
		/// <returns>true if an object could be found, false otherwise</returns>
		public bool FindBestMatch(PointOctree<T>.EntryFitnessCalculator fitnessCalculator, PointOctree<T>.NodeFilter nodeFilter, out T matchingObj, out float matchFitness, PointOctree<T>.EntryFilter filter = null) {
			bool foundObj = false;
			matchingObj = default;
			matchFitness = float.MaxValue;
			// ensure this node passes the filter
			if (!nodeFilter(this)) {
				return false;
			}
			// Check against any objects in this node
			if (filter == null) {
				foreach (var (obj,objPosition) in _entries) {
					if (!fitnessCalculator(obj, objPosition, out float objFitness)) {
						continue;
					}
					if (!foundObj || objFitness < matchFitness) {
						matchingObj = obj;
						matchFitness = objFitness;
						foundObj = true;
					}
				}
			} else {
				foreach (var (obj,objPosition) in _entries) {
					if (!filter(obj, objPosition)) {
						continue;
					}
					if (!fitnessCalculator(obj, objPosition, out float objFitness)) {
						continue;
					}
					if (!foundObj || objFitness < matchFitness) {
						matchingObj = obj;
						matchFitness = objFitness;
						foundObj = true;
					}
				}
			}
			// Check children
			if (_childNodes != null) {
				foreach (var childNode in _childNodes) {
					if (childNode != null && childNode.FindBestMatch(fitnessCalculator, nodeFilter, out T childNodeMatchObj, out float childNodeMatchFitness, filter)) {
						if (!foundObj || childNodeMatchFitness < matchFitness) {
							matchingObj = childNodeMatchObj;
							matchFitness = childNodeMatchFitness;
							foundObj = true;
						}
					}
				}
			}
			return foundObj;
		}
		#endregion



		#region Node Manipulation
		/// <summary>
		/// Set values for this node. 
		/// </summary>
		/// <param name="centerVal">Centre position of this node.</param>
		/// <param name="baseLengthVal">Length of this node, not taking looseness into account.</param>
		private void SetValues(Vector3 centerVal, float baseLengthVal) {
			_boxInfo = new BoxInfo(centerVal, baseLengthVal);
			if (_childBoxes == null) {
				_childBoxes = new BoxInfo[OctreeUtils.SubdivideCount];
			}
			float quarter = _boxInfo.length / 4f;
			float childLength = (_boxInfo.length / 2);
			for (int i = 0; i < OctreeUtils.SubdivideCount; i++) {
				OctreeNodeSector sector = (OctreeNodeSector)i;
				_childBoxes[i] = new BoxInfo(centerVal + (quarter * sector.GetDirection()), childLength);
			}
		}
		
		/// <summary>
		/// Set the 8 children of this octree.
		/// </summary>
		/// <param name="childOctrees">The 8 new child nodes.</param>
		internal void SetChildren(PointOctreeNode<T>[] childOctrees) {
			if (childOctrees.Length != 8) {
				Debug.LogError("Child octree array must be length 8. Was length: " + childOctrees.Length);
				return;
			}
			_childNodes = childOctrees;
			if (_childEntries == null) {
				_childEntries = new Dictionary<T,OctreeNodeSector>();
			} else {
				_childEntries.Clear();
			}
			OctreeNodeSector childSector = 0;
			foreach (var childNode in childOctrees) {
				if (childNode != null) {
					foreach (var entry in childNode.objectsInSelf) {
						_childEntries.Add(entry,childSector);
					}
					foreach (var entry in childNode.objectsInChildren) {
						_childEntries.Add(entry,childSector);
					}
				}
				childSector++;
			}
		}
		
		/// <summary>
		/// We can shrink the octree if:
		/// - This node is >= double minLength in length
		/// - All objects in the root node are within one octant
		/// - This node doesn't have children, or does but 7/8 children are empty
		/// We can also shrink it if there are no objects left at all!
		/// NOTE: This node will become invalid if a different node is returned
		/// </summary>
		/// <param name="minLength">Minimum dimensions of a node in this octree.</param>
		/// <returns>The new root, or the existing one if we didn't shrink.</returns>
		public PointOctreeNode<T> ShrinkIfPossible(float minLength) {
			if (_boxInfo.length < (2 * minLength)) {
				// can't shrink smaller than this
				return this;
			}
			if (_entries.Count == 0 && (_childNodes?.Length ?? 0) == 0) {
				// already have no entries and no children, so no need to shrink
				return this;
			}
			
			// Check objects in root
			int bestFit = -1;
			bool firstObj = true;
			foreach (var (_,objPosition) in _entries) {
				int newBestFit = (int)OctreeUtils.GetSector(objPosition - _boxInfo.center);
				if (firstObj || newBestFit == bestFit) {
					firstObj = false;
					// In same octant as the other(s). Does it fit completely inside that octant?
					if (_childBoxes[newBestFit].Encapsulates(objPosition)) {
						bestFit = newBestFit;
					} else {
						// Nope, so we can't reduce. Otherwise we continue
						return this;
					}
				} else {
					return this; // Can't reduce - objects fit in different octants
				}
			}
			
			// Check objects in children if there are any
			if (_childNodes != null) {
				bool childHadContent = false;
				int childCount = _childNodes.Length;
				for (int i=0; i<childCount; i++) {
					var childNode = _childNodes[i];
					if (childNode.HasAnyObjects()) {
						if (childHadContent) {
							return this; // Can't shrink - another child had content already
						}
						if (bestFit >= 0 && bestFit != i) {
							return this; // Can't reduce - objects in root are in a different octant to objects in child
						}
						childHadContent = true;
						bestFit = i;
					}
				}
				// No objects in the whole node or its children
				if (bestFit == -1) {
					return this;
				}
				// We have children. Use the appropriate child as the new root node
				var bestFitChild = _childNodes[bestFit];
				// add entries to child and remove from self (this will invalidate this node)
				_childEntries.Clear();
				foreach (var (obj, objBounds) in _entries) {
					bestFitChild.NoCheckAdd(obj, objBounds);
				}
				_entries.Clear();
				return bestFitChild;
			} else {
				if (bestFit == -1) {
					return this;
				}
				// We don't have any children, so just shrink this node to the new size
				// We already know that everything will still fit in it
				var childBox = _childBoxes[bestFit];
				SetValues(childBox.center, childBox.length / 2.0f);
				return this;
			}
		}
		
		/// <summary>
		/// Splits the octree into eight children.
		/// </summary>
		public void Split() {
			if (_childNodes != null) {
				return;
			}
			_childNodes = new PointOctreeNode<T>[OctreeUtils.SubdivideCount];
			if (_childEntries == null) {
				_childEntries = new Dictionary<T,OctreeNodeSector>();
			} else {
				_childEntries.Clear();
			}
			// Now that we have the new children, move this node's existing objects into them
			foreach (var (obj, objPosition) in _entries) {
				// Find which child the object is closest to based on where the
				// object's center is located in relation to the octree's center
				var childNode = GetBestFitChildNode(objPosition, out var childSector);
				childNode.NoCheckAdd(obj, objPosition);
				_childEntries[obj] = childSector;
			}
			_entries.Clear();
		}
		
		/// <summary>
		/// Merge all children into this node - the opposite of Split.
		/// Note: We only have to check one level down since a merge will never happen if the children already have children,
		/// since THAT won't happen unless there are already too many objects to merge.
		/// </summary>
		public void Merge() {
			if (_childNodes == null) {
				return;
			}
			foreach (var childNode in _childNodes) {
				if (childNode != null) {
					childNode.Merge();
					foreach (var (obj, objPosition) in childNode._entries) {
						_entries[obj] = objPosition;
					}
				}
			}
			// Remove the child nodes (and the objects in them - they've been added elsewhere now)
			_childNodes = null;
			_childEntries.Clear();
		}
		
		/// <summary>
		/// Checks if there are few enough objects in this node and its children that the children should all be merged into this.
		/// </summary>
		/// <returns>True there are less or the same abount of objects in this and its children than numObjectsAllowed.</returns>
		bool ShouldMerge() {
			if (_childNodes == null) {
				return false;
			}
			int totalObjects = _entries.Count + (_childEntries?.Count ?? 0);
			return totalObjects <= MaxNodeEntries;
		}
		#endregion
		
		
		#region Gizmos
		/// <summary>
		/// Draws node boundaries visually for debugging.
		/// Must be called from OnDrawGizmos externally. <seealso cref="DrawObjectPositionsGizmos"/>
		/// </summary>
		/// <param name="depth">The depth of this node within the octree</param>
		public void DrawNodeBoundsGizmos(int depth = 0) {
			var prevColor = Gizmos.color;
			float tintVal = Mathf.Clamp01((float)depth / 7.0f);
			Gizmos.color = new Color(tintVal, 0, 1.0f - tintVal);
			
			Gizmos.DrawWireCube(_boxInfo.bounds.center, _boxInfo.bounds.size);
			
			if (_childNodes != null) {
				depth++;
				foreach (var childNode in _childNodes) {
					if (childNode != null) {
						childNode.DrawNodeBoundsGizmos(depth);
					}
				}
			}
			
			Gizmos.color = prevColor;
		}
		
		/// <summary>
		/// Draws the bounds of all objects in the tree visually for debugging.
		/// Must be called from OnDrawGizmos externally. <seealso cref="DrawNodeBoundsGizmos"/>
		/// NOTE: marker.tif must be placed in your Unity /Assets/Gizmos subfolder for this to work.
		/// </summary>
		public void DrawObjectPositionsGizmos() {
			var prevColor = Gizmos.color;
			float tintVal = Mathf.Clamp01(_boxInfo.length / 20.0f);
			Gizmos.color = new Color(0, 1.0f - tintVal, tintVal, 0.25f);
			
			foreach (var (_,objPosition) in _entries) {
				Gizmos.DrawIcon(objPosition, "marker.tif", true);
			}
			
			if (_childNodes != null) {
				foreach (var childNode in _childNodes) {
					if (childNode != null) {
						childNode.DrawObjectPositionsGizmos();
					}
				}
			}
			
			Gizmos.color = prevColor;
		}
		#endregion
	}
}