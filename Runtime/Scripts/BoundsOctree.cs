using System.Collections.Generic;
using UnityEngine;

namespace Octrees
{
	// A Dynamic, Loose Octree for storing any objects that can be described with AABB bounds
	// See also: PointOctree, where objects are stored as single points and some code can be simplified
	// Octree:	An octree is a tree data structure which divides 3D space into smaller partitions (nodes)
	//			and places objects into the appropriate nodes. This allows fast access to objects
	//			in an area of interest without having to check every object.
	// Dynamic: The octree grows or shrinks as required when objects as added or removed
	//			It also splits and merges nodes as appropriate. There is no maximum depth.
	//			Nodes have a constant - numObjectsAllowed - which sets the amount of items allowed in a node before it splits.
	// Loose:	The octree's nodes can be larger than 1/2 their parent's length and width, so they overlap to some extent.
	//			This can alleviate the problem of even tiny objects ending up in large nodes if they're near boundaries.
	//			A looseness value of 1.0 will make it a "normal" octree.
	// T:		The content of the octree can be anything, since the bounds data is supplied separately.
	
	// Originally written for my game Scraps (http://www.scrapsgame.com) but intended to be general-purpose.
	// Copyright 2014 Nition, BSD licence (see LICENCE file). www.momentstudio.co.nz
	// Unity-based, but could be adapted to work in pure C#
	
	// Note: For loops are often used here since in some cases (e.g. the IsColliding method)
	// they actually give much better performance than using Foreach, even in the compiled build.
	// Using a LINQ expression is worse again than Foreach.
	public class BoundsOctree<T> {
		public const int DefaultMaxGrowAttempts = 20;
		
		// Root node of the octree
		private BoundsOctreeNode<T> rootNode;
		
		// Should be a value between 1 and 2. A multiplier for the base size of a node.
		// 1.0 is a "normal" octree, while values > 1 have overlap
		public readonly float looseness;
		
		// Size that the octree was on creation
		public readonly float initialSize;
		
		// Minimum side length that a node can be - essentially an alternative to having a max depth
		public readonly float minNodeSize;

		/// <summary>
		/// Constructor for the bounds octree.
		/// </summary>
		/// <param name="initialSize">Size of the sides of the initial node, in metres. The octree will never shrink smaller than this.</param>
		/// <param name="initialCenter">Position of the centre of the initial node.</param>
		/// <param name="minNodeSize">Nodes will stop splitting if the new nodes would be smaller than this (metres).</param>
		/// <param name="looseness">A multiplier for what ratio of a nodes size should it be extended to. Should be between 0 and 1</param>
		public BoundsOctree(float initialSize, Vector3 initialCenter, float minNodeSize, float looseness) {
			if (minNodeSize > initialSize) {
				Debug.LogWarning($"Minimum node size must be at least as big as the initial size. Was: {minNodeSize} Adjusted to: {initialSize}");
				minNodeSize = initialSize;
			}
			this.initialSize = initialSize;
			this.minNodeSize = minNodeSize;
			this.looseness = Mathf.Clamp(looseness, 1.0f, 2.0f);
			this.rootNode = new BoundsOctreeNode<T>(this, initialCenter, initialSize);
		}
		
		/// <summary>
		/// The strict bounds of the root node of the octree
		/// </summary>
		public Bounds bounds => rootNode.bounds;
		
		/// <summary>
		/// The loose bounds of the root node of the octree
		/// </summary>
		public Bounds looseBounds => rootNode.looseBounds;
		
		/// <summary>
		/// The number of objects in this octree
		/// </summary>
		public int Count => rootNode.Count;
		
		/// <summary>
		/// Tells if the octree contains the given object
		/// </summary>
		/// <param name="obj">The object to check</param>
		/// <returns>true if the given object is contained in the octree, false otherwise</returns>
		public bool Contains(T obj) => rootNode.Contains(obj);
		
		/// <summary>
		/// Add an object.
		/// </summary>
		/// <param name="obj">Object to add.</param>
		/// <param name="objBounds">3D bounding box around the object.</param>
		/// <param name="maxGrowAttempts">The maximum number of times the octree should try to grow to encapsulate the given object</param>
		/// <returns>true if the object could be added, false if it could not</returns>
		public bool Add(T obj, Bounds objBounds, int maxGrowAttempts = DefaultMaxGrowAttempts) {
			if (rootNode.Remove(obj)) {
				Debug.LogWarning("Calling BoundsOctree.Add when an entry already exists. Use AddOrMove instead");
			}
			// Add object or expand the octree until it can be added
			if (maxGrowAttempts == 0) {
				return rootNode.Add(obj, objBounds);
			}
			int count = 0; // Safety check against infinite/excessive growth
			while (!rootNode.Add(obj, objBounds)) {
				Grow(objBounds.center - rootNode.center);
				count++;
				if (count >= maxGrowAttempts) {
					Debug.LogError($"Aborted Add operation as it seemed to be going on forever ({count}) attempts at growing the octree.");
					return false;
				}
			}
			return true;
		}
		
		/// <summary>
		/// Remove an object. Makes the assumption that the object only exists once in the tree.
		/// </summary>
		/// <param name="obj">Object to remove.</param>
		/// <param name="mergeIfAble">Whether octree nodes should get merged if they're able to be</param>
		/// <returns>True if the object was removed successfully.</returns>
		public bool Remove(T obj, bool mergeIfAble = true) {
			bool removed = rootNode.Remove(obj, mergeIfAble:mergeIfAble);
			// See if we can shrink the octree down now that we've removed the item
			if (removed && mergeIfAble) {
				ShrinkIfPossible();
			}
			return removed;
		}
		
		/// <summary>
		/// Move an entry in the octree if able
		/// </summary>
		/// <param name="obj">The object to move within the octree</param>
		/// <param name="newObjBounds">The object's new bounds</param>
		/// <param name="maxGrowAttempts">The maximum number of times the octree should try to grow to encapsulate the given object</param>
		/// <param name="mergeIfAble">Whether octree nodes should get merged if they're able to be</param>
		/// <returns>The result of moving the object within the octree</returns>
		public OctreeMoveResult Move(T obj, Bounds newObjBounds, int maxGrowAttempts = DefaultMaxGrowAttempts, bool mergeIfAble = true) {
			var moveResult = rootNode.Move(obj, newObjBounds, isRoot:true, mergeIfAble:mergeIfAble);
			switch (moveResult) {
				case OctreeMoveResult.None:
					return OctreeMoveResult.None;
				
				case OctreeMoveResult.Removed:
					if (Add(obj, newObjBounds, maxGrowAttempts: maxGrowAttempts)) {
						return OctreeMoveResult.Moved;
					}
					return OctreeMoveResult.Removed;
				
				case OctreeMoveResult.Moved:
					return OctreeMoveResult.Moved;
			}
			Debug.LogError($"Unknown octree move result {moveResult}");
			return OctreeMoveResult.None;
		}
		
		/// <summary>
		/// Adds or moves the given object within the octree
		/// </summary>
		/// <param name="obj">The object to move or add</param>
		/// <param name="objBounds">The bounds of the object</param>
		/// <param name="maxGrowAttempts">The maximum number of times the octree should try to grow to encapsulate the given object</param>
		/// <param name="mergeIfAble">Whether octree nodes should get merged if they're able to be</param>
		/// <returns>true if the object resides in the octree after this operation, false if it does not</returns>
		public bool AddOrMove(T obj, Bounds objBounds, int maxGrowAttempts = DefaultMaxGrowAttempts, bool mergeIfAble = true) {
			var moveResult = Move(obj, objBounds, maxGrowAttempts:maxGrowAttempts, mergeIfAble:mergeIfAble);
			switch (moveResult) {
				case OctreeMoveResult.None:
					return Add(obj, objBounds, maxGrowAttempts:maxGrowAttempts);
				
				case OctreeMoveResult.Removed:
					return false;
				
				case OctreeMoveResult.Moved:
					return true;
			}
			Debug.LogError($"Unknown octree move result {moveResult}");
			return false;
		}
		
		/// <summary>
		/// Check if the specified bounds intersect with anything in the tree. See also: GetColliding.
		/// </summary>
		/// <param name="checkBounds">bounds to check.</param>
		/// <returns>True if there was a collision.</returns>
		public bool IsIntersecting(in Bounds checkBounds) {
			#if UNITY_EDITOR
			debugDrawOptions?.DebugDrawBoundsIntersect(checkBounds);
			#endif
			return rootNode.IsIntersecting(checkBounds);
		}
		
		/// <summary>
		/// Returns an array of objects that intersect with the specified bounds, if any. Otherwise returns an empty array. See also: IsColliding.
		/// </summary>
		/// <param name="checkBounds">bounds to check.</param>
		/// <param name="intersections">The resulting list of intersecting objects.</param>
		/// <returns>Objects that intersect with the specified bounds.</returns>
		public bool GetIntersecting(in Bounds checkBounds, ref List<T> intersections) {
			#if UNITY_EDITOR
			debugDrawOptions?.DebugDrawBoundsIntersect(checkBounds);
			#endif
			return rootNode.GetIntersecting(checkBounds, ref intersections) > 0;
		}
		
		/// <summary>
		/// Check if the specified ray intersects with anything in the tree. See also: GetColliding.
		/// </summary>
		/// <param name="checkRay">ray to check.</param>
		/// <param name="maxDistance">distance to check.</param>
		/// <returns>True if there was a collision.</returns>
		public bool Raycast(in Ray checkRay, float maxDistance) {
			#if UNITY_EDITOR
			debugDrawOptions?.DebugDrawRaycast(checkRay, maxDistance);
			#endif
			return rootNode.IsRayIntersecting(checkRay, maxDistance);
		}
		
		/// <summary>
		/// Gets an array of objects that intersect with the specified ray, if any.
		/// </summary>
		/// <param name="checkRay">The ray to check intersections against</param>
		/// <param name="collidingWith">The resulting list of intersecting objects</param>
		/// <param name="maxDistance">The maximum distance to cast the ray</param>
		/// <returns>Objects that intersect with the specified ray.</returns>
		public bool Raycast(in Ray checkRay, ref List<T> collidingWith, float maxDistance = float.PositiveInfinity) {
			#if UNITY_EDITOR
			debugDrawOptions?.DebugDrawRaycast(checkRay, maxDistance);
			#endif
			return rootNode.GetRayIntersecting(checkRay, ref collidingWith, maxDistance) > 0;
		}
		
		/// <summary>
		/// Find all the nodes that are visible in the given camera frustum
		/// </summary>
		/// <param name="camera">The camera viewing the octree</param>
		/// <returns>A list of objects in the octree in view of the camera</returns>
		public List<T> GetWithinFrustum(Camera camera) {
			var planes = GeometryUtility.CalculateFrustumPlanes(camera);
			var list = new List<T>();
			rootNode.GetWithinFrustum(planes, ref list);
			return list;
		}

		/// <summary>
		/// Grow the octree to fit in all objects.
		/// </summary>
		/// <param name="direction">Direction to grow.</param>
		private void Grow(Vector3 direction) {
			int xDirection = direction.x >= 0 ? 1 : -1;
			int yDirection = direction.y >= 0 ? 1 : -1;
			int zDirection = direction.z >= 0 ? 1 : -1;
			BoundsOctreeNode<T> oldRoot = rootNode;
			float halfLen = rootNode.baseLength / 2.0f;
			float newLength = rootNode.baseLength * 2.0f;
			Vector3 newCenter = rootNode.center + new Vector3(xDirection * halfLen, yDirection * halfLen, zDirection * halfLen);
			// Create a new, bigger octree root node
			var newRootNode = new BoundsOctreeNode<T>(this, newCenter, newLength);
			if (oldRoot.HasAnyObjects()) {
				// Create 7 new octree children to go with the old root as children of the new root
				var rootSector = OctreeUtils.GetSector(oldRoot.center - newCenter);
				var children = new BoundsOctreeNode<T>[OctreeUtils.SubdivideCount];
				children[(int)rootSector] = oldRoot;
				// Attach the new children to the new root node
				newRootNode.SetChildren(children);
			}
			rootNode = newRootNode;
		}
		
		/// <summary>
		/// Shrink the octree if possible, else leave it the same.
		/// </summary>
		private void ShrinkIfPossible() {
			rootNode = rootNode.ShrinkIfPossible(initialSize);
		}
		
		
		
		#region Gizmos / Debug Draw
		/// <summary>
		/// Draws node boundaries visually for debugging.
		/// Must be called from OnDrawGizmos externally. See also: DrawAllObjects.
		/// </summary>
		public void DrawNodeBoundsGizmos() {
			rootNode.DrawNodeBoundsGizmos();
		}

		/// <summary>
		/// Draws the bounds of all objects in the tree visually for debugging.
		/// Must be called from OnDrawGizmos externally. See also: DrawAllBounds.
		/// </summary>
		public void DrawObjectBoundsGizmos() {
			rootNode.DrawObjectBoundsGizmos();
		}
		
		#if UNITY_EDITOR
		public struct DebugDrawOptions {
			public float duration;
			public Color boundsIntersectColor;
			public Color raycastColor;
			public OctreeUtils.PointConverter treeToWorldPointConverter;
			
			public static DebugDrawOptions Default => new DebugDrawOptions() {
				duration = 1.0f,
				boundsIntersectColor = Color.green,
				raycastColor = Color.green
			};

			public void DebugDrawBoundsIntersect(Bounds bounds) {
				OctreeUtils.DebugDrawBounds(bounds, boundsIntersectColor, duration, depthTest:true, treeToWorldPointConverter);
			}
			
			public void DebugDrawRaycast(Ray ray, float distance) {
				if (treeToWorldPointConverter != null) {
					Vector3 newOrigin = treeToWorldPointConverter(ray.origin);
					Vector3 newDirEndPoint = treeToWorldPointConverter(ray.origin + ray.direction);
					var newDir = (newDirEndPoint - newOrigin);
					ray = new Ray(newOrigin, newDir);
				}
				if (distance < (float.MaxValue / 2.0f)) {
					Debug.DrawRay(ray.origin, (ray.direction * distance), raycastColor, duration, depthTest:true);
				}
				else {
					Debug.DrawRay(ray.origin, ray.direction, raycastColor, duration, depthTest:true);
				}
			}
		}
		
		public DebugDrawOptions? debugDrawOptions;
		#endif
		#endregion
	}
}