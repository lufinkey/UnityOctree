using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Octrees
{
	// A node in a BoundsOctree
	// Copyright 2014 Nition, BSD licence (see LICENCE file). www.momentstudio.co.nz
	public class BoundsOctreeNode<T>
	{
		public readonly BoundsOctree<T> tree;
		
		// Centre of this node
		public Vector3 center { get; private set; }
		
		// Length of this node if it has a looseness of 1.0
		public float baseLength { get; private set; }
		
		// Actual length of sides, taking the looseness value into account
		public float adjustedLength { get; private set; }
		
		// Bounding box that represents this node
		public Bounds bounds { get; private set; }
		
		// Objects in this node
		public IReadOnlyCollection<T> objects => _objects.Keys;
		private Dictionary<T,Bounds> _objects = new Dictionary<T,Bounds>();
		
		// Objects in the children of this node
		public IReadOnlyCollection<T> objectsInChildren => _objectsInChildren;
		private HashSet<T> _objectsInChildren = new HashSet<T>();
		
		// Child nodes, if any
		public IReadOnlyList<BoundsOctreeNode<T>> children => _children;
		private BoundsOctreeNode<T>[] _children = null;
		
		public bool hasChildren => _children != null;

		// Bounds of potential children to this node. These are actual size (with looseness taken into account), not base size
		Bounds[] childBounds;

		// If there are already NUM_OBJECTS_ALLOWED in a node, we split it into children
		// A generally good number seems to be something around 8-15
		const int NUM_OBJECTS_ALLOWED = 8;

		// An object in the octree
		struct OctreeObject
		{
			public T Obj;
			public Bounds Bounds;
		}

		/// <summary>
		/// Constructor.
		/// </summary>
		/// <param name="baseLengthVal">Length of this node, not taking looseness into account.</param>
		/// <param name="centerVal">Centre position of this node.</param>
		public BoundsOctreeNode(BoundsOctree<T> tree, float baseLengthVal, Vector3 centerVal) {
			this.tree = tree;
			SetValues(baseLengthVal, centerVal);
		}
		
		/// <summary>
		/// The number of objects contained in this node and its children
		/// </summary>
		public int Count => (_objects.Count + _objectsInChildren.Count);
		
		/// <summary>
		/// Tells if this node contains the given object
		/// </summary>
		/// <param name="obj">The object to check</param>
		/// <returns>true if the node or its children contains the object, false otherwise</returns>
		public bool Contains(T obj) {
			return _objects.ContainsKey(obj) || _objectsInChildren.Contains(obj);
		}

		/// <summary>
		/// Add an object.
		/// </summary>
		/// <param name="obj">Object to add.</param>
		/// <param name="objBounds">3D bounding box around the object.</param>
		/// <returns>True if the object fits entirely within this node.</returns>
		public bool Add(T obj, Bounds objBounds) {
			if (!Encapsulates(bounds, objBounds)) {
				return false;
			}
			// We always put things in the deepest possible child
			// So we can skip some checks if there are children aleady
			if (_children == null) {
				// Just add if few objects are here, or children would be below min size
				if (_objects.Count < NUM_OBJECTS_ALLOWED || (baseLength / 2) <= tree.minSize) {
					_objects[obj] = bounds;
					return true;
				}
				// Fits at this level, but we can go deeper. Would it fit there?
				Split();
			}
			// Find which child node the object should reside in
			int bestFitChildIndex = BestFitChild(objBounds.center);
			var bestFitChildNode = _children[bestFitChildIndex];
			if (bestFitChildNode.Add(obj, objBounds)) {
				_objectsInChildren.Add(obj);
			} else {
				// Didn't fit in a child. We'll have to it to this node instead
				_objects[obj] = objBounds;
			}
			return true;
		}

		/// <summary>
		/// Remove an object. Makes the assumption that the object only exists once in the tree.
		/// </summary>
		/// <param name="obj">Object to remove.</param>
		/// <param name="mergeIfAble">Whether child nodes should get merged if they're able to be</param>
		/// <returns>True if the object was removed successfully.</returns>
		public bool Remove(T obj, bool mergeIfAble = true) {
			bool removed = false;
			// try to remove object from self
			if (_objects.Remove(obj)) {
				removed = true;
			}
			// try to remove object from children
			else if (_children != null && _objectsInChildren.Contains(obj)) {
				foreach (var childNode in _children) {
					// remove from child node
					if (childNode.Remove(obj)) {
						removed = true;
						_objectsInChildren.Remove(obj);
						break;
					}
				}
			}
			if (removed && _children != null) {
				// Check if we should merge nodes now that we've removed an item
				if (mergeIfAble && ShouldMerge()) {
					Merge();
				}
			}
			return removed;
		}

		/// <summary>
		/// Attempt to move an object somewhere else in this node. If the object is contained but cannot be re-added, it will just be removed
		/// </summary>
		/// <param name="obj">The object to move</param>
		/// <param name="newObjBounds">The new object bounds</param>
		/// <param name="mergeIfAble">Merge any mergeable nodes in the octree</param>
		/// <returns>The result of attempting to move the object</returns>
		public OctreeMoveResult Move(T obj, Bounds newObjBounds, bool mergeIfAble = true) {
			// try to move from this node
			if (_objects.Remove(obj)) {
				if (Add(obj, newObjBounds)) {
					return OctreeMoveResult.Moved;
				}
				return OctreeMoveResult.Removed;
			}
			// try to move from children
			if (_children != null && _objectsInChildren.Contains(obj)) {
				foreach (var childNode in _children) {
					var moveResult = childNode.Move(obj, newObjBounds);
					switch (moveResult) {
						case OctreeMoveResult.None:
							break;
						
						case OctreeMoveResult.Removed:
							// try adding to other children if encapsulated
							if (Encapsulates(newObjBounds)) {
								foreach (var otherChildNode in _children) {
									if (otherChildNode == childNode) {
										continue;
									}
									if (otherChildNode.Add(obj, newObjBounds)) {
										return OctreeMoveResult.Moved;
									}
								}
								_objects[obj] = newObjBounds;
								_objectsInChildren.Remove(obj);
								if (mergeIfAble && ShouldMerge()) {
									Merge();
								}
								return OctreeMoveResult.Moved;
							} else {
								_objectsInChildren.Remove(obj);
								if (mergeIfAble && ShouldMerge()) {
									Merge();
								}
								return OctreeMoveResult.Removed;
							}
						
						case OctreeMoveResult.Moved:
							// object was moved
							return OctreeMoveResult.Moved;
					}
				}
			}
			// object is not in this node
			return OctreeMoveResult.None;
		}
		
		/// <summary>
		/// Find the node that fully encapsulates the given bounds
		/// </summary>
		/// <param name="innerBounds">The inner bounds to check</param>
		/// <returns>The node which fully encapsulates the given bounds, or null if no node fully encapsulates the given bounds</returns>
		public BoundsOctreeNode<T> FindEncapsulatingNode(in Bounds innerBounds) {
			if (!Encapsulates(innerBounds)) {
				return null;
			}
			if (_children != null) {
				foreach (var childNode in _children) {
					var encapsulatingNode = childNode.FindEncapsulatingNode(innerBounds);
					if (encapsulatingNode != null) {
						return encapsulatingNode;
					}
				}
			}
			return this;
		}
		
		/// <summary>
		/// Check if the specified bounds intersect with anything in the tree. See also: GetColliding.
		/// </summary>
		/// <param name="checkBounds">Bounds to check.</param>
		/// <returns>True if there was a collision.</returns>
		public bool IsColliding(in Bounds checkBounds) {
			// Are the input bounds at least partially in this node?
			if (!bounds.Intersects(checkBounds)) {
				return false;
			}
			// Check against any objects in this node
			foreach (var pair in _objects) {
				if (pair.Value.Intersects(checkBounds)) {
					return true;
				}
			}
			// Check children
			if (_children != null)
			{
				for (int i = 0; i < 8; i++)
				{
					if (_children[i].IsColliding(checkBounds))
					{
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>
		/// Check if the specified ray intersects with anything in the tree. See also: GetColliding.
		/// </summary>
		/// <param name="checkRay">Ray to check.</param>
		/// <param name="maxDistance">Distance to check.</param>
		/// <returns>True if there was a collision.</returns>
		public bool IsRayColliding(in Ray checkRay, float maxDistance = float.PositiveInfinity) {
			// Is the input ray at least partially in this node?
			float distance;
			if (!bounds.IntersectRay(checkRay, out distance) || distance > maxDistance) {
				return false;
			}
			// Check against any objects in this node
			foreach (var pair in _objects) {
				if (pair.Value.IntersectRay(checkRay, out distance) && distance <= maxDistance) {
					return true;
				}
			}
			// Check children
			if (_children != null) {
				foreach (var childNode in _children) {
					if (childNode.IsRayColliding(checkRay, maxDistance)) {
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>
		/// Returns an array of objects that intersect with the specified bounds, if any. Otherwise returns an empty array. See also: IsColliding.
		/// </summary>
		/// <param name="checkBounds">Bounds to check. Passing by ref as it improves performance with structs.</param>
		/// <param name="result">List result.</param>
		/// <returns>Objects that intersect with the specified bounds.</returns>
		public void GetColliding(in Bounds checkBounds, List<T> result) {
			// Are the input bounds at least partially in this node?
			if (!bounds.Intersects(checkBounds)) {
				return;
			}
			// Check against any objects in this node
			foreach (var pair in _objects) {
				if (pair.Value.Intersects(checkBounds)) {
					result.Add(pair.Key);
				}
			}
			// Check children
			if (_children != null) {
				foreach (var childNode in _children) {
					childNode.GetColliding(checkBounds, result);
				}
			}
		}

		/// <summary>
		/// Returns an array of objects that intersect with the specified ray, if any. Otherwise returns an empty array. See also: IsColliding.
		/// </summary>
		/// <param name="checkRay">Ray to check. Passing by ref as it improves performance with structs.</param>
		/// <param name="maxDistance">Distance to check.</param>
		/// <param name="result">List result.</param>
		/// <returns>Objects that intersect with the specified ray.</returns>
		public void GetRayColliding(in Ray checkRay, List<T> result, float maxDistance) {
			float distance;
			// Is the input ray at least partially in this node?
			if (!bounds.IntersectRay(checkRay, out distance) || distance > maxDistance) {
				return;
			}
			// Check against any objects in this node
			foreach (var pair in _objects) {
				if (pair.Value.IntersectRay(checkRay, out distance) && distance <= maxDistance) {
					result.Add(pair.Key);
				}
			}
			// Check children
			if (_children != null) {
				foreach (var childNode in _children) {
					childNode.GetRayColliding(checkRay, result, maxDistance);
				}
			}
		}
		
		public void GetWithinFrustum(Plane[] planes, List<T> result) {
			// Is the input node inside the frustum?
			if (!GeometryUtility.TestPlanesAABB(planes, bounds)) {
				return;
			}
			// Check against any objects in this node
			foreach (var pair in _objects) {
				if (GeometryUtility.TestPlanesAABB(planes, pair.Value)) {
					result.Add(pair.Key);
				}
			}
			// Check children
			if (_children != null) {
				foreach (var childNode in _children) {
					childNode.GetWithinFrustum(planes, result);
				}
			}
		}
		
		/// <summary>
		/// Set the 8 children of this octree.
		/// </summary>
		/// <param name="childOctrees">The 8 new child nodes.</param>
		public void SetChildren(BoundsOctreeNode<T>[] childOctrees) {
			if (childOctrees.Length != 8) {
				Debug.LogError("Child octree array must be length 8. Was length: " + childOctrees.Length);
				return;
			}
			_children = childOctrees;
		}

		/// <summary>
		/// Draws node boundaries visually for debugging.
		/// Must be called from OnDrawGizmos externally. See also: DrawAllObjects.
		/// </summary>
		/// <param name="depth">Used for recurcive calls to this method.</param>
		public void DrawAllBounds(float depth = 0)
		{
			float tintVal = depth / 7; // Will eventually get values > 1. Color rounds to 1 automatically
			Gizmos.color = new Color(tintVal, 0, 1.0f - tintVal);

			Bounds thisBounds = new Bounds(center, new Vector3(adjustedLength, adjustedLength, adjustedLength));
			Gizmos.DrawWireCube(thisBounds.center, thisBounds.size);

			if (_children != null)
			{
				depth++;
				for (int i = 0; i < 8; i++)
				{
					_children[i].DrawAllBounds(depth);
				}
			}

			Gizmos.color = Color.white;
		}

		/// <summary>
		/// Draws the bounds of all objects in the tree visually for debugging.
		/// Must be called from OnDrawGizmos externally. See also: DrawAllBounds.
		/// </summary>
		public void DrawAllObjects()
		{
			float tintVal = baseLength / 20;
			Gizmos.color = new Color(0, 1.0f - tintVal, tintVal, 0.25f);

			foreach (var pair in _objects) {
				var objBounds = pair.Value;
				Gizmos.DrawCube(objBounds.center, objBounds.size);
			}
			
			if (_children != null) {
				for (int i = 0; i < 8; i++) {
					_children[i].DrawAllObjects();
				}
			}
			Gizmos.color = Color.white;
		}

		/// <summary>
		/// We can shrink the octree if:
		/// - This node is >= double minLength in length
		/// - All objects in the root node are within one octant
		/// - This node doesn't have children, or does but 7/8 children are empty
		/// We can also shrink it if there are no objects left at all!
		/// </summary>
		/// <param name="minLength">Minimum dimensions of a node in this octree.</param>
		/// <returns>The new root, or the existing one if we didn't shrink.</returns>
		public BoundsOctreeNode<T> ShrinkIfPossible(float minLength)
		{
			if (baseLength < (2 * minLength)) {
				return this;
			}
			if (objects.Count == 0 && (_children == null || _children.Length == 0)) {
				return this;
			}
			
			// Check objects in root
			int bestFit = -1;
			bool firstObj = true;
			foreach (var pair in _objects) {
				var obj = pair.Key;
				var objBounds = pair.Value;
				int newBestFit = BestFitChild(objBounds.center);
				if (firstObj || newBestFit == bestFit) {
					firstObj = false;
					// In same octant as the other(s). Does it fit completely inside that octant?
					if (Encapsulates(childBounds[newBestFit], objBounds)) {
						if (bestFit < 0) {
							bestFit = newBestFit;
						}
					} else {
						// Nope, so we can't reduce. Otherwise we continue
						return this;
					}
				} else {
					return this; // Can't reduce - objects fit in different octants
				}
			}

			// Check objects in children if there are any
			if (_children != null)
			{
				bool childHadContent = false;
				for (int i = 0; i < _children.Length; i++)
				{
					if (_children[i].HasAnyObjects())
					{
						if (childHadContent)
						{
							return this; // Can't shrink - another child had content already
						}

						if (bestFit >= 0 && bestFit != i)
						{
							return this; // Can't reduce - objects in root are in a different octant to objects in child
						}

						childHadContent = true;
						bestFit = i;
					}
				}
			}

			// Can reduce
			if (_children == null)
			{
				// We don't have any children, so just shrink this node to the new size
				// We already know that everything will still fit in it
				SetValues(baseLength / 2, childBounds[bestFit].center);
				return this;
			}

			// No objects in entire octree
			if (bestFit == -1)
			{
				return this;
			}

			// We have children. Use the appropriate child as the new root node
			return _children[bestFit];
		}

		/// <summary>
		/// Find which child node this object would be most likely to fit in.
		/// </summary>
		/// <param name="objBounds">The object's bounds.</param>
		/// <returns>One of the eight child octants.</returns>
		public int BestFitChild(Vector3 objBoundsCenter)
		{
			return (objBoundsCenter.x <= center.x ? 0 : 1)
			       + (objBoundsCenter.y >= center.y ? 0 : 4)
			       + (objBoundsCenter.z <= center.z ? 0 : 2);
		}

		/// <summary>
		/// Checks if this node or anything below it has something in it.
		/// </summary>
		/// <returns>True if this node or any of its children, grandchildren etc have something in them</returns>
		public bool HasAnyObjects()
		{
			if (objects.Count > 0) return true;

			if (_children != null)
			{
				for (int i = 0; i < 8; i++)
				{
					if (_children[i].HasAnyObjects()) return true;
				}
			}

			return false;
		}

		/*
		/// <summary>
		/// Get the total amount of objects in this node and all its children, grandchildren etc. Useful for debugging.
		/// </summary>
		/// <param name="startingNum">Used by recursive calls to add to the previous total.</param>
		/// <returns>Total objects in this node and its children, grandchildren etc.</returns>
		public int GetTotalObjects(int startingNum = 0) {
			int totalObjects = startingNum + objects.Count;
			if (children != null) {
				for (int i = 0; i < 8; i++) {
					totalObjects += children[i].GetTotalObjects();
				}
			}
			return totalObjects;
		}
		*/

		/// <summary>
		/// Set values for this node. 
		/// </summary>
		/// <param name="baseLengthVal">Length of this node, not taking looseness into account.</param>
		/// <param name="loosenessVal">Multiplier for baseLengthVal to get the actual size.</param>
		/// <param name="centerVal">Centre position of this node.</param>
		private void SetValues(float baseLengthVal, Vector3 centerVal) {
			baseLength = baseLengthVal;
			center = centerVal;
			adjustedLength = tree.looseness * baseLengthVal;

			// Create the bounding box.
			Vector3 size = new Vector3(adjustedLength, adjustedLength, adjustedLength);
			bounds = new Bounds(center, size);
			
			float quarter = baseLength / 4f;
			float childActualLength = (baseLength / 2) * tree.looseness;
			Vector3 childActualSize = new Vector3(childActualLength, childActualLength, childActualLength);
			childBounds = new Bounds[8];
			childBounds[0] = new Bounds(center + new Vector3(-quarter, quarter, -quarter), childActualSize);
			childBounds[1] = new Bounds(center + new Vector3(quarter, quarter, -quarter), childActualSize);
			childBounds[2] = new Bounds(center + new Vector3(-quarter, quarter, quarter), childActualSize);
			childBounds[3] = new Bounds(center + new Vector3(quarter, quarter, quarter), childActualSize);
			childBounds[4] = new Bounds(center + new Vector3(-quarter, -quarter, -quarter), childActualSize);
			childBounds[5] = new Bounds(center + new Vector3(quarter, -quarter, -quarter), childActualSize);
			childBounds[6] = new Bounds(center + new Vector3(-quarter, -quarter, quarter), childActualSize);
			childBounds[7] = new Bounds(center + new Vector3(quarter, -quarter, quarter), childActualSize);
		}

		/// <summary>
		/// Splits the octree into eight children.
		/// </summary>
		public void Split() {
			if (_children != null) {
				return;
			}
			float quarter = baseLength / 4f;
			float newLength = baseLength / 2;
			// Create the 8 children
			_children = new BoundsOctreeNode<T>[8];
			_children[0] = new BoundsOctreeNode<T>(tree, newLength, center + new Vector3(-quarter, quarter, -quarter));
			_children[1] = new BoundsOctreeNode<T>(tree, newLength, center + new Vector3(quarter, quarter, -quarter));
			_children[2] = new BoundsOctreeNode<T>(tree, newLength, center + new Vector3(-quarter, quarter, quarter));
			_children[3] = new BoundsOctreeNode<T>(tree, newLength, center + new Vector3(quarter, quarter, quarter));
			_children[4] = new BoundsOctreeNode<T>(tree, newLength, center + new Vector3(-quarter, -quarter, -quarter));
			_children[5] = new BoundsOctreeNode<T>(tree, newLength, center + new Vector3(quarter, -quarter, -quarter));
			_children[6] = new BoundsOctreeNode<T>(tree, newLength, center + new Vector3(-quarter, -quarter, quarter));
			_children[7] = new BoundsOctreeNode<T>(tree, newLength, center + new Vector3(quarter, -quarter, quarter));
			// Now that we have the new children, see if this node's existing objects would fit there
			foreach (var pair in _objects.ToArray()) {
				var existingObj = pair.Key;
				var objBounds = pair.Value;
				// Find which child the object is closest to based on where the
				// object's center is located in relation to the octree's center
				int bestFitChildIndex = BestFitChild(objBounds.center);
				var bestFitChildNode = _children[bestFitChildIndex];
				// Does it fit?
				if (bestFitChildNode.Add(existingObj, objBounds)) {
					_objectsInChildren.Add(existingObj);
					_objects.Remove(existingObj);
				}
			}
		}

		/// <summary>
		/// Merge all children into this node - the opposite of Split.
		/// Note: We only have to check one level down since a merge will never happen if the children already have children,
		/// since THAT won't happen unless there are already too many objects to merge.
		/// </summary>
		public void Merge() {
			if (_children == null) {
				return;
			}
			foreach (var childNode in _children) {
				childNode.Merge();
				foreach (var pair in childNode._objects) {
					_objects[pair.Key] = pair.Value;
				}
			}
			// Remove the child nodes (and the objects in them - they've been added elsewhere now)
			_children = null;
		}
		
		/// <summary>
		/// Checks if this node encapsulates innerBounds.
		/// </summary>
		/// <param name="innerBounds">The inner bounds to check</param>
		/// <returns>true if innerBounds is fully encapsulated in this node</returns>
		public bool Encapsulates(Bounds innerBounds) => Encapsulates(this.bounds, innerBounds);
		
		/// <summary>
		/// Checks if outerBounds encapsulates innerBounds.
		/// </summary>
		/// <param name="outerBounds">Outer bounds.</param>
		/// <param name="innerBounds">Inner bounds.</param>
		/// <returns>True if innerBounds is fully encapsulated by outerBounds.</returns>
		private static bool Encapsulates(Bounds outerBounds, Bounds innerBounds) {
			return outerBounds.Contains(innerBounds.min) && outerBounds.Contains(innerBounds.max);
		}

		/// <summary>
		/// Checks if there are few enough objects in this node and its children that the children should all be merged into this.
		/// </summary>
		/// <returns>True there are less or the same abount of objects in this and its children than numObjectsAllowed.</returns>
		public bool ShouldMerge()
		{
			int totalObjects = objects.Count;
			if (_children != null)
			{
				foreach (BoundsOctreeNode<T> child in _children)
				{
					if (child._children != null)
					{
						// If any of the *children* have children, there are definitely too many to merge,
						// or the child woudl have been merged already
						return false;
					}

					totalObjects += child.objects.Count;
				}
			}

			return totalObjects <= NUM_OBJECTS_ALLOWED;
		}
	}
}