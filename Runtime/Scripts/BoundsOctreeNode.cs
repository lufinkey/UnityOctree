using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Octrees
{
	// A node in a BoundsOctree
	// Copyright 2014 Nition, BSD licence (see LICENCE file). www.momentstudio.co.nz
	public class BoundsOctreeNode<T> {
		// If there are already MaxNodeEntries in a node, we split it into children
		// A generally good number seems to be something around 8-15
		const int MaxNodeEntries = 8;
		
		private struct BoxInfo {
			public Vector3 center;
			public float length;
			public Bounds strictBounds;
			public Bounds looseBounds;

			public BoxInfo(Vector3 center, float length, float looseness) {
				this.center = center;
				this.length = length;
				this.strictBounds = new Bounds(center, new Vector3(length, length, length));
				float looseLength = length + (looseness * length);
				this.looseBounds = new Bounds(center, new Vector3(looseLength, looseLength, looseLength));
			}
			
			public bool LooseEncapsulates(in Bounds cmpBounds) {
				// check that loose bounds fully encapsulates the given bounds
				return looseBounds.Contains(cmpBounds.min) && looseBounds.Contains(cmpBounds.max);
			}
			
			public bool Encapsulates(in Bounds cmpBounds) {
				return LooseEncapsulates(cmpBounds)
				       // check that the center of the given bounds is within the strict bounds
				       && strictBounds.Contains(cmpBounds.center);
			}
		}
		
		public readonly BoundsOctree<T> tree;
		
		// Size/positioning info for this node
		private BoxInfo _boxInfo;
		
		// the object entries for this node
		private Dictionary<T,Bounds> _entries = new Dictionary<T,Bounds>();
		// The child entries for this node
		private Dictionary<T,OctreeNodeSector> _childEntries;
		// The subdivided nodes within this node
		private BoundsOctreeNode<T>[] _childNodes;
		// Size/positioning info of potential children to this node
		private BoxInfo[] _childBoxes;

		/// <summary>
		/// Constructs a new bounds octree node
		/// </summary>
		/// <param name="tree">The tree that this node belongs to</param>
		/// <param name="center">The center position of this node</param>
		/// <param name="size">The length of a side this node, not taking looseness into account.</param>
		public BoundsOctreeNode(BoundsOctree<T> tree, Vector3 center, float size) {
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
		/// The strict bounds of this octree node
		/// </summary>
		public Bounds bounds => _boxInfo.strictBounds;
		
		/// <summary>
		/// The loose bounds of this octree node
		/// </summary>
		public Bounds looseBounds => _boxInfo.looseBounds;
		
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
		public IReadOnlyList<BoundsOctreeNode<T>> childNodes => _childNodes;
		
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
		/// Attempts to get the bounds of an entry in this node or its children
		/// </summary>
		/// <param name="obj">The entry to get the bounds for</param>
		/// <param name="objBounds">The bounds of the object entry</param>
		/// <returns>true if the object was found in the octree node or its children, false if it was not</returns>
		public bool TryGetBounds(T obj, out Bounds objBounds) {
			if (_entries.TryGetValue(obj, out objBounds)) {
				return true;
			} else if (_childEntries != null && _childEntries.TryGetValue(obj, out var entrySector)) {
				return _childNodes[(int)entrySector].TryGetBounds(obj, out objBounds);
			}
			objBounds = default;
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
		/// Add or set an entry in this node or its children
		/// </summary>
		/// <param name="obj">The object to add</param>
		/// <param name="objBounds">3D bounding box around the object.</param>
		/// <returns>True if the object fits entirely within this node</returns>
		public bool Add(T obj, in Bounds objBounds) {
			if (!_boxInfo.LooseEncapsulates(objBounds)) {
				return false;
			}
			if (Remove(obj, isRoot:false, mergeIfAble:false)) {
				Debug.LogWarning("Calling Add when obj is already contained within node");
			}
			NoCheckAdd(obj, objBounds);
			return true;
		}
		
		/// <summary>
		/// Add or set an entry in this node or its children without checking current added state or encapsulation
		/// </summary>
		/// <param name="obj">The object to add</param>
		/// <param name="objBounds">3D bounding box around the object</param>
		private void NoCheckAdd(T obj, in Bounds objBounds) {
			// We always put things in the deepest possible child
			// So we can skip some checks if there are children aleady
			if (_childNodes == null) {
				// Just add if few objects are here, or children would be below min size
				if (_entries.Count < MaxNodeEntries || (_boxInfo.length / 2.0f) < tree.minNodeSize) {
					_entries[obj] = objBounds;
					return;
				}
				Split();
			}
			// Find which child node the object should reside in
			if (!GetBestFitChildNode(objBounds, out var childNode, out var childSector)) {
				_entries[obj] = objBounds;
				return;
			}
			childNode.NoCheckAdd(obj, objBounds);
			_childEntries[obj] = childSector;
		}
		
		/// <summary>
		/// Remove an object. Makes the assumption that the object only exists once in the tree.
		/// </summary>
		/// <param name="obj">The object to remove</param>
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
			if (removed && mergeIfAble && !isRoot && ShouldMerge()) {
				Merge();
			}
			return removed;
		}

		/// <summary>
		/// Attempt to move an object somewhere else in this node. If the object is contained but cannot be re-added, it will just be removed
		/// </summary>
		/// <param name="obj">The object to move</param>
		/// <param name="newObjBounds">The new object bounds</param>
		/// <param name="isRoot">If true, a check against the center of the object bounds of only this node (not its children) will be made. If false, no check will be made</param>
		/// <param name="mergeIfAble">Merge any mergeable nodes in the octree</param>
		/// <returns>The result of attempting to move the object</returns>
		public OctreeMoveResult Move(T obj, Bounds newObjBounds, bool isRoot, bool mergeIfAble = true) {
			// try to move within this node
			if (_entries.Remove(obj)) {
				if (_boxInfo.LooseEncapsulates(newObjBounds)) {
					NoCheckAdd(obj, newObjBounds);
					return OctreeMoveResult.Moved;
				}
				// entry was removed, so merge if able
				if (!isRoot && mergeIfAble && ShouldMerge()) {
					Merge();
				}
				return OctreeMoveResult.Removed;
			}
			// try to move from children
			if (_childEntries != null && _childEntries.TryGetValue(obj, out var oldObjSector)) {
				var oldChildNode = _childNodes[(int)oldObjSector];
				var newObjSector = OctreeUtils.GetSector(newObjBounds.center - _boxInfo.center);
				if (newObjSector == oldObjSector) {
					// old sector matches new sector, so move within child
					var childMoveResult = oldChildNode.Move(obj, newObjBounds, isRoot:false, mergeIfAble:mergeIfAble);
					switch (childMoveResult) {
						case OctreeMoveResult.None:
							return OctreeMoveResult.None;
						
						case OctreeMoveResult.Removed:
							// removed from child
							_childEntries.Remove(obj);
							if (_boxInfo.LooseEncapsulates(newObjBounds)) {
								// still fits within this node
								_entries[obj] = newObjBounds;
								return OctreeMoveResult.Moved;
							}
							return OctreeMoveResult.Removed;
						
						case OctreeMoveResult.Moved:
							return OctreeMoveResult.Moved;
					}
					Debug.LogError("Unknown child move result "+childMoveResult);
				} else {
					// sector has changed, so remove from old child node
					oldChildNode.Remove(obj, isRoot:isRoot, mergeIfAble:mergeIfAble);
					_childEntries.Remove(obj);
					// re-add if still strictly inside this node, or if root (loose encapsulation is okay on root)
					if (isRoot ? _boxInfo.LooseEncapsulates(newObjBounds) : _boxInfo.Encapsulates(newObjBounds)) {
						NoCheckAdd(obj, newObjBounds);
						return OctreeMoveResult.Moved;
					}
					// entry was removed, so merge if able
					if (!isRoot && mergeIfAble && ShouldMerge()) {
						Merge();
					}
					return OctreeMoveResult.Removed;
				}
			}
			return OctreeMoveResult.None;
		}
		#endregion
		
		
		
		#region Encapsulation
		/// <summary>
		/// Checks if the given bounds can be encapsulated in the bounds of this node
		/// </summary>
		/// <param name="checkBounds">The bounds to check if encapsulated</param>
		/// <returns>true if the given bounds are encapsulated in this node, false otherwise</returns>
		public bool Encapsulates(in Bounds checkBounds) => _boxInfo.Encapsulates(checkBounds);
		
		/// <summary>
		/// Checks if the given bounds can be encapsulated in the loose bounds this node
		/// </summary>
		/// <param name="checkBounds">The bounds to check if encapsulated</param>
		/// <returns>true if the given bounds are encapsulated in this node, false otherwise</returns>
		public bool LooseEncapsulates(in Bounds checkBounds) => _boxInfo.LooseEncapsulates(checkBounds);
		
		/// <summary>
		/// Finds the child node that encapsulates the given bounds, if any
		/// </summary>
		/// <param name="checkBounds">The bounds to find the encapsulating sector for</param>
		/// <param name="childNode">The child node that encapsulates the bounds</param>
		/// <param name="childSector">The sector of the child node</param>
		/// <returns>true if a node is found, false if it wasn't</returns>
		private bool GetBestFitChildNode(in Bounds checkBounds, out BoundsOctreeNode<T> childNode, out OctreeNodeSector childSector) {
			childSector = OctreeUtils.GetSector(checkBounds.center - _boxInfo.center);
			int childIndex = (int)childSector;
			var childBoxInfo = _childBoxes[childIndex];
			if (!childBoxInfo.LooseEncapsulates(checkBounds)) {
				childNode = null;
				return false;
			}
			childNode = _childNodes[childIndex];
			if (childNode == null) {
				childNode = new BoundsOctreeNode<T>(tree, childBoxInfo.center, childBoxInfo.length);
				_childNodes[childIndex] = childNode;
			}
			return true;
		}
		
		/// <summary>
		/// Find the node that fully encapsulates the given bounds
		/// </summary>
		/// <param name="checkBounds">The inner bounds to check if encapsulated</param>
		/// <returns>The node which fully encapsulates the given bounds, or null if no node fully encapsulates the given bounds</returns>
		public BoundsOctreeNode<T> FindEncapsulatingNode(in Bounds checkBounds) {
			if (_boxInfo.LooseEncapsulates(checkBounds)) {
				if (_childNodes != null) {
					// check best fit child
					var childSector = OctreeUtils.GetSector(checkBounds.center - _boxInfo.center);
					var childNode = _childNodes[(int)childSector];
					if (childNode != null) {
						var encapsulatingNode = childNode.FindEncapsulatingNode(checkBounds);
						if (encapsulatingNode != null) {
							return encapsulatingNode;
						}
					}
				}
				return this;
			}
			return null;
		}
		#endregion
		
		
		
		#region Intersection / Search
		/// <summary>
		/// Check if the specified bounds intersect with anything in the tree. See also: GetColliding.
		/// </summary>
		/// <param name="checkBounds">Bounds to check.</param>
		/// <param name="filter">Optionally filters which entries in the tree should be checked for intersection</param>
		/// <returns>True if there was a collision.</returns>
		public bool IsIntersecting(in Bounds checkBounds, BoundsOctree<T>.EntryFilter filter = null) {
			// Are the input bounds at least partially in this node?
			if (!_boxInfo.looseBounds.Intersects(checkBounds)) {
				return false;
			}
			// Check against any objects in this node
			if (filter == null) {
				foreach (var pair in _entries) {
					if (pair.Value.Intersects(checkBounds)) {
						return true;
					}
				}
			} else {
				foreach (var pair in _entries) {
					if (!filter(pair.Key, pair.Value)) {
						continue;
					}
					if (pair.Value.Intersects(checkBounds)) {
						return true;
					}
				}
			}
			// Check children
			if (_childNodes != null) {
				foreach (var childNode in _childNodes) {
					if (childNode != null && childNode.IsIntersecting(checkBounds, filter)) {
						return true;
					}
				}
			}
			return false;
		}
		
		/// <summary>
		/// Gets an array of objects that intersect with the specified bounds, if any.
		/// </summary>
		/// <param name="checkBounds">The bounds to check intersection against</param>
		/// <param name="results">The list of objects intersecting the given bounds</param>
		/// <param name="filter">Optionally filters which entries in the tree should be checked for intersection</param>
		/// <returns>The total number of intersections in this node or its children</returns>
		public int GetIntersecting(in Bounds checkBounds, ref List<T> results, BoundsOctree<T>.EntryFilter filter = null) {
			// Are the input bounds at least partially in this node?
			if (!_boxInfo.looseBounds.Intersects(checkBounds)) {
				return 0;
			}
			int totalIntersections = 0;
			// Check against any objects in this node
			if (filter == null) {
				foreach (var pair in _entries) {
					if (pair.Value.Intersects(checkBounds)) {
						if (results == null) {
							results = new List<T>();
						}
						results.Add(pair.Key);
						totalIntersections++;
					}
				}
			} else {
				foreach (var pair in _entries) {
					if (!filter(pair.Key, pair.Value)) {
						continue;
					}
					if (pair.Value.Intersects(checkBounds)) {
						if (results == null) {
							results = new List<T>();
						}
						results.Add(pair.Key);
						totalIntersections++;
					}
				}
			}
			// Check children
			if (_childNodes != null) {
				foreach (var childNode in _childNodes) {
					if (childNode != null) {
						totalIntersections += childNode.GetIntersecting(checkBounds, ref results, filter);
					}
				}
			}
			return totalIntersections;
		}
		
		/// <summary>
		/// Check if the specified ray intersects with anything in the tree. <seealso cref="IsIntersecting"/>
		/// </summary>
		/// <param name="checkRay">The ray to check intersections against</param>
		/// <param name="maxDistance">The maximum distance to cast the ray</param>
		/// <param name="filter">Optionally filters which entries in the tree should be checked for intersection</param>
		/// <returns>True if there was a collision.</returns>
		public bool IsRayIntersecting(in Ray checkRay, float maxDistance, BoundsOctree<T>.EntryFilter filter = null) {
			// Is the input ray at least partially in this node?
			if (!_boxInfo.looseBounds.IntersectRay(checkRay, out float distance) || distance > maxDistance) {
				return false;
			}
			// Check against any objects in this node
			if (filter == null) {
				foreach (var pair in _entries) {
					if (pair.Value.IntersectRay(checkRay, out distance) && distance <= maxDistance) {
						return true;
					}
				}
			} else {
				foreach (var pair in _entries) {
					if (!filter(pair.Key, pair.Value)) {
						continue;
					}
					if (pair.Value.IntersectRay(checkRay, out distance) && distance <= maxDistance) {
						return true;
					}
				}
			}
			// Check children
			if (_childNodes != null) {
				foreach (var childNode in _childNodes) {
					if (childNode != null && childNode.IsRayIntersecting(checkRay, maxDistance, filter)) {
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>
		/// Gets an array of objects that intersect with the specified ray, if any.
		/// </summary>
		/// <param name="checkRay">The ray to check intersections against</param>
		/// <param name="maxDistance">The maximum distance to cast the ray</param>
		/// <param name="results">The objects intersecting with the ray</param>
		/// <param name="filter">Optionally filters which entries in the tree should be checked for intersection</param>
		/// <returns>The total number of ray intersections in this node or its children</returns>
		public int GetRayIntersecting(in Ray checkRay, ref List<T> results, float maxDistance, BoundsOctree<T>.EntryFilter filter = null) {
			// Is the input ray at least partially in this node?
			if (!_boxInfo.looseBounds.IntersectRay(checkRay, out float distance) || distance > maxDistance) {
				return 0;
			}
			int totalIntersections = 0;
			// Check against any objects in this node
			if (filter == null) {
				foreach (var pair in _entries) {
					if (pair.Value.IntersectRay(checkRay, out distance) && distance <= maxDistance) {
						if (results == null) {
							results = new List<T>();
						}
						results.Add(pair.Key);
						totalIntersections++;
					}
				}
			} else {
				foreach (var pair in _entries) {
					if (!filter(pair.Key, pair.Value)) {
						continue;
					}
					if (pair.Value.IntersectRay(checkRay, out distance) && distance <= maxDistance) {
						if (results == null) {
							results = new List<T>();
						}
						results.Add(pair.Key);
						totalIntersections++;
					}
				}
			}
			// Check children
			if (_childNodes != null) {
				foreach (var childNode in _childNodes) {
					if (childNode != null) {
						totalIntersections += childNode.GetRayIntersecting(checkRay, ref results, maxDistance, filter);
					}
				}
			}
			return totalIntersections;
		}
		
		/// <summary>
		/// Gets all the objects that lie within the given frustum planes
		/// </summary>
		/// <param name="planes">The planes that represent the frustum</param>
		/// <param name="results">The objects from this node or its children that are inside the frustum</param>
		/// <param name="filter">Optionally filters which entries in the tree should be checked against the frustum</param>
		/// <returns>The total number of objects that are inside the frustum</returns>
		public int GetWithinFrustum(Plane[] planes, ref List<T> results, BoundsOctree<T>.EntryFilter filter = null) {
			// Is the input node inside the frustum?
			if (!GeometryUtility.TestPlanesAABB(planes, _boxInfo.looseBounds)) {
				return 0;
			}
			int totalInFrustum = 0;
			// Check against any objects in this node
			if (filter == null) {
				foreach (var pair in _entries) {
					if (GeometryUtility.TestPlanesAABB(planes, pair.Value)) {
						if (results == null) {
							results = new List<T>();
						}
						results.Add(pair.Key);
						totalInFrustum++;
					}
				}
			} else {
				foreach (var pair in _entries) {
					if (!filter(pair.Key, pair.Value)) {
						continue;
					}
					if (GeometryUtility.TestPlanesAABB(planes, pair.Value)) {
						if (results == null) {
							results = new List<T>();
						}
						results.Add(pair.Key);
						totalInFrustum++;
					}
				}
			}
			// Check children
			if (_childNodes != null) {
				foreach (var childNode in _childNodes) {
					if (childNode != null) {
						totalInFrustum += childNode.GetWithinFrustum(planes, ref results);
					}
				}
			}
			return totalInFrustum;
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
		public bool FindBestMatch(BoundsOctree<T>.EntryFitnessCalculator fitnessCalculator, BoundsOctree<T>.NodeFilter nodeFilter, out T matchingObj, out float matchFitness, BoundsOctree<T>.EntryFilter filter = null) {
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
			_boxInfo = new BoxInfo(centerVal, baseLengthVal, tree.looseness);
			if (_childBoxes == null) {
				_childBoxes = new BoxInfo[OctreeUtils.SubdivideCount];
			}
			float quarter = _boxInfo.length / 4f;
			float childLength = (_boxInfo.length / 2);
			for (int i = 0; i < OctreeUtils.SubdivideCount; i++) {
				OctreeNodeSector sector = (OctreeNodeSector)i;
				_childBoxes[i] = new BoxInfo(centerVal + (quarter * sector.GetDirection()), childLength, tree.looseness);
			}
		}
		
		/// <summary>
		/// Set the 8 children of this octree.
		/// </summary>
		/// <param name="childOctrees">The 8 new child nodes.</param>
		internal void SetChildren(BoundsOctreeNode<T>[] childOctrees) {
			if (childOctrees.Length != 8) {
				Debug.LogError("Child octree array must be length 8. Was length: " + childOctrees.Length);
				return;
			}
			_childNodes = childOctrees;
			if (_childEntries == null) {
				_childEntries = new Dictionary<T, OctreeNodeSector>();
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
		public BoundsOctreeNode<T> ShrinkIfPossible(float minLength) {
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
			foreach (var (_,objBounds) in _entries) {
				int newBestFit = (int)OctreeUtils.GetSector(objBounds.center - _boxInfo.center);
				if (firstObj || newBestFit == bestFit) {
					firstObj = false;
					// In same octant as the other(s). Does it fit completely inside that octant?
					if (_childBoxes[newBestFit].LooseEncapsulates(objBounds)) {
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
					if (childNode != null && childNode.HasAnyObjects()) {
						if (childHadContent) {
							return this; // Can't shrink - another child had content already
						}
						if (bestFit != -1 && bestFit != i) {
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
			}
			else {
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
			_childNodes = new BoundsOctreeNode<T>[OctreeUtils.SubdivideCount];
			if (_childEntries == null) {
				_childEntries = new Dictionary<T, OctreeNodeSector>();
			} else {
				_childEntries.Clear();
			}
			// Now that we have the new children, see if this node's existing objects would fit there
			foreach (var (obj, objBounds) in _entries.ToArray()) {
				// Find which child the object is closest to based on where the
				// object's center is located in relation to the octree's center
				if (GetBestFitChildNode(objBounds, out var childNode, out var childSector)) {
					childNode.NoCheckAdd(obj, objBounds);
					_entries.Remove(obj);
					_childEntries[obj] = childSector;
				}
			}
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
					foreach (var (obj, objBounds) in childNode._entries) {
						_entries[obj] = objBounds;
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
		public bool ShouldMerge() {
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
		/// Must be called from OnDrawGizmos externally. See also: DrawAllObjects.
		/// </summary>
		/// <param name="depth">The depth of this node within the octree</param>
		public void DrawNodeBoundsGizmos(int depth = 0) {
			var prevColor = Gizmos.color;
			float tintVal =  Mathf.Clamp01((float)depth / 7.0f);
			Gizmos.color = new Color(tintVal, 0, 1.0f - tintVal);
			
			Gizmos.DrawWireCube(_boxInfo.strictBounds.center, _boxInfo.strictBounds.size);
			
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
		/// Must be called from OnDrawGizmos externally. See also: DrawAllBounds.
		/// </summary>
		public void DrawObjectBoundsGizmos() {
			var prevColor = Gizmos.color;
			float tintVal = Mathf.Clamp01(_boxInfo.length / 20.0f);
			Gizmos.color = new Color(0, 1.0f - tintVal, tintVal, 0.25f);

			foreach (var pair in _entries) {
				var objBounds = pair.Value;
				Gizmos.DrawCube(objBounds.center, objBounds.size);
			}
			
			if (_childNodes != null) {
				foreach (var childNode in _childNodes) {
					if (childNode != null) {
						childNode.DrawObjectBoundsGizmos();
					}
				}
			}
			
			Gizmos.color = prevColor;
		}
		#endregion
	}
}
