using System.Collections.Generic;
using UnityEngine;

namespace Octrees
{
	// A Dynamic Octree for storing any objects that can be described as a single point
	// See also: BoundsOctree, where objects are described by AABB bounds
	// Octree:	An octree is a tree data structure which divides 3D space into smaller partitions (nodes)
	//			and places objects into the appropriate nodes. This allows fast access to objects
	//			in an area of interest without having to check every object.
	// Dynamic: The octree grows or shrinks as required when objects as added or removed
	//			It also splits and merges nodes as appropriate. There is no maximum depth.
	//			Nodes have a constant - numObjectsAllowed - which sets the amount of items allowed in a node before it splits.
	// T:		The content of the octree can be anything, since the bounds data is supplied separately.

	// Originally written for my game Scraps (http://www.scrapsgame.com) but intended to be general-purpose.
	// Copyright 2014 Nition, BSD licence (see LICENCE file). www.momentstudio.co.nz
	// Unity-based, but could be adapted to work in pure C#
	public class PointOctree<T> {
		public struct NearbyEntry {
			public T obj;
			public Vector3 position;
			public float sqrDistance;

			public NearbyEntry(T obj, in Vector3 position, float sqrDistance) {
				this.obj = obj;
				this.position = position;
				this.sqrDistance = sqrDistance;
			}
		}
		
		public delegate bool EntryFilter(T obj, Vector3 objPosition);
		public delegate bool NodeFilter(in PointOctreeNode<T> node);
		public delegate bool EntryFitnessCalculator(T obj, in Vector3 point, out float fitness);
		
		public const int DefaultMaxGrowAttempts = 20;
		
		// Size that the octree was on creation
		public readonly float initialSize;

		// Minimum side length that a node can be - essentially an alternative to having a max depth
		public readonly float minNodeSize;
		
		// Root node of the octree
		private PointOctreeNode<T> _rootNode;
		
		/// <summary>
		/// Constructor for the point octree.
		/// </summary>
		/// <param name="initialSize">Size of the sides of the initial node. The octree will never shrink smaller than this.</param>
		/// <param name="initialCenter">Position of the centre of the initial node.</param>
		/// <param name="minNodeSize">Nodes will stop splitting if the new nodes would be smaller than this.</param>
		public PointOctree(float initialSize, Vector3 initialCenter, float minNodeSize) {
			if (minNodeSize > initialSize) {
				Debug.LogWarning($"Minimum node size must be at least as big as the initial size. Was: {minNodeSize} Adjusted to: {initialSize}");
				minNodeSize = initialSize;
			}
			this.initialSize = initialSize;
			this.minNodeSize = minNodeSize;
			_rootNode = new PointOctreeNode<T>(this, initialCenter, initialSize);
		}
		
		
		
		#region Properties
		/// <summary>
		/// The strict bounds of the root node of the octree
		/// </summary>
		public Bounds bounds => _rootNode.bounds;
		
		/// <summary>
		/// The number of objects in this octree
		/// </summary>
		public int Count => _rootNode.Count;
		#endregion
		
		
		
		#region Add / Remove / Move / Access
		/// <summary>
		/// Gets all the objects in this octree
		/// </summary>
		public IEnumerable<T> GetAll() => _rootNode.GetAll();
		
		/// <summary>
		/// Tells if the octree contains the given object
		/// </summary>
		/// <param name="obj">The object to check</param>
		/// <returns>true if the given object is contained in the octree, false otherwise</returns>
		public bool Contains(T obj) => _rootNode.Contains(obj);
		
		/// <summary>
		/// Add an object to the octree, moving an existing entry if it already exists.
		/// </summary>
		/// <param name="obj">The object to add to the tree</param>
		/// <param name="objPos">The position of the object</param>
		/// <param name="maxGrowAttempts">The maximum number of times the octree should try to grow to encapsulate the given object</param>
		/// <returns>true if the object could be added, false if it could not</returns>
		public bool Add(T obj, Vector3 objPos, int maxGrowAttempts = DefaultMaxGrowAttempts) {
			// Add object or expand the octree until it can be added
			if (maxGrowAttempts == 0) {
				return _rootNode.Add(obj, objPos);
			}
			int count = 0; // Safety check against infinite/excessive growth
			while (!_rootNode.Add(obj, objPos)) {
				Grow(objPos - _rootNode.center);
				count++;
				if (count >= maxGrowAttempts) {
					Debug.LogError($"Add operation took too long. ({count}) attempts at growing the octree.");
					return false;
				}
			}
			return true;
		}
		
		/// <summary>
		/// Remove an object from the octree.
		/// </summary>
		/// <param name="obj">The object to remove</param>
		/// <param name="mergeIfAble">Controls whether octree nodes should get merged if they're able to be</param>
		/// <returns>True if the object was removed successfully.</returns>
		public bool Remove(T obj, bool mergeIfAble = true) {
			bool removed = _rootNode.Remove(obj, isRoot:true, mergeIfAble:mergeIfAble);
			// See if we can shrink the octree down now that we've removed the item
			if (removed && mergeIfAble) {
				ShrinkIfPossible();
			}
			return removed;
		}
		
		// TODO add Move and AddOrMove operations
		#endregion
		
		
		
		#region Search / Intersection
		/// <summary>
		/// Returns objects that are within <paramref name="maxDistance"/> of the specified ray.
		/// If none, returns false. Uses supplied list for results.
		/// </summary>
		/// <param name="ray">The ray to compare distance to</param>
		/// <param name="maxDistance">Maximum distance from the ray to consider</param>
		/// <param name="nearBy">The list to populate with the nearby objects</param>
		/// <param name="filter">Optionally filters objects to include (return true to include the object, false to not include it)</param>
		/// <returns>True if items are found, false if not</returns>
		public bool GetNearby(in Ray ray, float maxDistance, ref List<T> nearBy, EntryFilter filter = null) {
			nearBy?.Clear();
			return _rootNode.GetNearby(ray, maxDistance, ref nearBy, filter) > 0;
		}
		
		/// <summary>
		/// Returns objects that are within <paramref name="maxDistance"/> of the specified position.
		/// If none, returns false. Uses supplied list for results.
		/// </summary>
		/// <param name="position">The position to compare distances with</param>
		/// <param name="maxDistance">Maximum distance from the position to consider</param>
		/// <param name="nearBy">Pre-initialized list to populate</param>
		/// <param name="filter">Optionally filters objects to consider (return true to consider the object, false to not consider it)</param>
		/// <returns>True if items are found, false if not</returns>
		public bool GetNearby(in Vector3 position, float maxDistance, ref List<T> nearBy, EntryFilter filter = null) {
			nearBy?.Clear();
			return _rootNode.GetNearby(position, maxDistance, ref nearBy, filter) > 0;
		}
		
		/// <summary>
		/// Returns objects that are within <paramref name="maxDistance"/> of the specified position, including the objects distances.
		/// If none, returns false. Uses supplied list for results.
		/// </summary>
		/// <param name="position">The position to compare distance to</param>
		/// <param name="maxDistance">Maximum distance from the given position to look for objects</param>
		/// <param name="nearBy">The list to populate with the nearby objects</param>
		/// <param name="filter">Optionally filters objects to consider (return true to consider the object, false to not consider it)</param>
		/// <returns>True if items are found, false if not</returns>
		public bool GetNearbyWithDistances(in Vector3 position, float maxDistance, ref List<NearbyEntry> nearBy, EntryFilter filter = null) {
			nearBy?.Clear();
			return _rootNode.GetNearbyWithDistances(position, maxDistance, ref nearBy, filter) > 0;
		}
		
		// A stupid temp implementation of this thing.
		public void GetNearbyN(Vector3 position, ref List<NearbyEntry> output, int desiredCount, float startingDistance) {
			if (Count <= desiredCount)
				return;
			
			float maxDistance = startingDistance;
			while (true)
			{
				GetNearbyWithDistances(position, maxDistance, ref output);
				if (output.Count >= desiredCount)
					break;
				maxDistance += startingDistance;
				Debug.Log("Another iteration");
			}
		}
		
		// A stupid temp implementation of this thing.
		public bool GetClosest(in Vector3 position, out T closestEntry, float startingSearchDistance = float.MaxValue, float maxSearchDistance = float.PositiveInfinity, List<NearbyEntry> cache = null) {
			if (cache == null) {
				cache = new List<NearbyEntry>();
			}
			// increase distance until we find any result
			float searchDistance = startingSearchDistance;
			bool lastIteration = false;
			while (true) {
				GetNearbyWithDistances(position, searchDistance, ref cache);
				if (cache.Count > 0) {
					break;
				} else if (lastIteration) {
					// no entries found in search radius
					closestEntry = default;
					return false;
				}
				searchDistance *= 2.0f;
				if (searchDistance >= maxSearchDistance) {
					searchDistance = maxSearchDistance;
					lastIteration = true;
				}
			}
			// find closest result
			int minIndex = -1;
			float minSqrDistance = float.PositiveInfinity;
			for (int i = 0; i < cache.Count; i++) {
				float sqrDist = cache[i].sqrDistance;
				if (minIndex == -1 || sqrDist < minSqrDistance) {
					minIndex = i;
					minSqrDistance = sqrDist;
				}
			}
			closestEntry = cache[minIndex].obj;
			return true;
		}
		
		/// <summary>
		/// Finds the best matching object in the octree using a given fitness calculator and node filter
		/// </summary>
		/// <param name="fitnessCalculator">Calculates the "fitness" of entries (how similar it is to the desired result). Lower return values are ranked as more "fit".</param>
		/// <param name="nodeFilter">Determines which nodes should be searched</param>
		/// <param name="matchingObj">The closest match found in the Octree, or `default` if not found</param>
		/// <param name="matchFitness">The calculated "fitness" value of the matching object</param>
		/// <param name="filter">Optionally filters objects to consider (return true to consider the object, false to not consider it)</param>
		/// <returns>true if an object could be found, false otherwise</returns>
		public bool FindBestMatch(EntryFitnessCalculator fitnessCalculator, NodeFilter nodeFilter, out T matchingObj, out float matchFitness, EntryFilter filter = null) {
			return _rootNode.FindBestMatch(fitnessCalculator, nodeFilter, out matchingObj, out matchFitness, filter);
		}
		
		/// <summary>
		/// Finds the closest point in the given (2d) view direction relative to the given camera
		/// </summary>
		/// <param name="origin">A 3D position in tree-space to compare against</param>
		/// <param name="viewDirection">The view direction to search (relative to the camera)</param>
		/// <param name="minDotProduct">The minimum allowed dot product between the direction from the origin to an object and the view direction</param>
		/// <param name="convertTreePointToWorld">Optionally converts a position in tree-space to world-space</param>
		/// <param name="camera">The camera viewing the points</param>
		/// <param name="closestObj">The closest object found in the Octree, or `default` if not found</param>
		/// <param name="closestDistance">The square distance of the closest object to the origin (in 2D view space)</param>
		/// <param name="filter">Optionally filters objects to consider (return true to consider the object, false to not consider it)</param>
		/// <returns>true if an object could be found, false otherwise</returns>
		public bool FindClosestInViewDirection(Vector3 origin, Vector2 viewDirection, float minDotProduct, OctreeUtils.PointConverter convertTreePointToWorld, Camera camera, out T closestObj, out float closestDistance, EntryFilter filter = null) {
			var cameraViewportRect = new Rect(0, 0, 1, 1);
			viewDirection = viewDirection.normalized;
			var originInViewport = camera.WorldToViewportPoint(convertTreePointToWorld?.Invoke(origin) ?? origin);
			var originInViewport2D = new Vector2(originInViewport.x, originInViewport.y);
			// fitness will be the square 2D distance from the given origin to the object position
			EntryFitnessCalculator fitnessCalculator = (T _, in Vector3 objPosition, out float distance) => {
				var viewportPoint = camera.WorldToViewportPoint(convertTreePointToWorld?.Invoke(objPosition) ?? objPosition);
				if (viewportPoint.z <= 0 || viewportPoint.x < 0 || viewportPoint.x > 1 || viewportPoint.y < 0 || viewportPoint.y > 1) {
					distance = float.MaxValue;
					return false;
				}
				var pointDirection = (viewportPoint - originInViewport);
				var pointDirection2D = new Vector2(pointDirection.x, pointDirection.y);
				float dotProduct = Vector2.Dot(viewDirection, pointDirection2D.normalized);
				if (dotProduct < minDotProduct) {
					distance = float.MaxValue;
					return false;
				}
				distance = pointDirection2D.sqrMagnitude;
				return true;
			};
			// filter bounds outside the viewport
			NodeFilter boundsFilter = (in PointOctreeNode<T> node) => {
				var boundsInViewport = OctreeUtils.CalculateBoundsInViewport(node.bounds, camera, convertTreePointToWorld);
				var viewportBoundsMin = boundsInViewport.min;
				var viewportBoundsMax = boundsInViewport.max;
				if (viewportBoundsMax.z <= 0) {
					return false;
				}
				// get the 2D rectangle for the checking bounds
				var rectInViewport = Rect.MinMaxRect(viewportBoundsMin.x, viewportBoundsMin.y, viewportBoundsMax.x, viewportBoundsMax.y);
				if (!cameraViewportRect.Overlaps(rectInViewport)) {
					return false;
				}
				// if rectangle contains the origin, we should check these bounds
				if (rectInViewport.Contains(originInViewport2D)) {
					return true;
				}
				// gets the closest point of the 
				var closestPoint = GetClosestEdgePoint(rectInViewport, origin);
				var pointDirection2D = (closestPoint - originInViewport2D);
				float dotProduct = Vector2.Dot(viewDirection, pointDirection2D.normalized);
				if (dotProduct >= minDotProduct) {
					return true;
				}
				foreach (var point2D in GetRectComparisonPoints(rectInViewport)) {
					pointDirection2D = (point2D - originInViewport2D);
					dotProduct = Vector2.Dot(viewDirection, pointDirection2D.normalized);
					if (dotProduct >= minDotProduct) {
						return true;
					}
				}
				return false;
			};
			return FindBestMatch(fitnessCalculator, boundsFilter, out closestObj, out closestDistance, filter);
		}
		
		public static IEnumerable<Vector2> GetRectComparisonPoints(Rect rect) {
			Vector2 min = rect.min;
			Vector2 max = rect.max;
			Vector2 topLeft = min;
			Vector2 topRight = new Vector2(max.x, min.y);
			Vector2 bottomLeft = new Vector2(min.x, max.y);
			Vector2 bottomRight = max;
			
			yield return topLeft; // Top left corner
			yield return topRight; // Top right corner
			yield return bottomLeft; // Bottom left corner
			yield return bottomRight; // Bottom right corner
			yield return rect.center;
		}
		
		public static Vector2 GetClosestEdgePoint(Rect rect, Vector2 point) {
			float minX = rect.xMin;
			float minY = rect.yMin;
			float maxX = rect.xMax;
			float maxY = rect.yMax;
			
			// Clamp the point to the rectangle bounds
			Vector2 clampedPoint = new Vector2(Mathf.Clamp(point.x, minX, maxX), Mathf.Clamp(point.y, minY, maxY));
			
			// Calculate the distance from the clamped point to each edge of the rectangle
			float distToLeft = Mathf.Abs(minX - clampedPoint.x);
			float distToRight = Mathf.Abs(maxX - clampedPoint.x);
			float distToBottom = Mathf.Abs(minY - clampedPoint.y);
			float distToTop = Mathf.Abs(maxY - clampedPoint.y);
			
			// Find the minimum distance
			float minDist = Mathf.Min(distToLeft, distToRight, distToBottom, distToTop);
			
			// Return the closest edge point
			if (minDist == distToLeft) {
				return new Vector2(minX, clampedPoint.y);
			} else if (minDist == distToRight) {
				return new Vector2(maxX, clampedPoint.y);
			} else if (minDist == distToBottom) {
				return new Vector2(clampedPoint.x, minY);
			} else { // minDist == distToTop
				return new Vector2(clampedPoint.x, maxY);
			}
		}
		#endregion
		
		
		
		#region Node Manipulation
		/// <summary>
		/// Grow the octree to fit in all objects.
		/// </summary>
		/// <param name="direction">Direction to grow.</param>
		public void Grow(Vector3 direction) {
			int xDirection = direction.x >= 0 ? 1 : -1;
			int yDirection = direction.y >= 0 ? 1 : -1;
			int zDirection = direction.z >= 0 ? 1 : -1;
			var oldRoot = _rootNode;
			float halfLen = oldRoot.baseLength / 2.0f;
			float newLength = oldRoot.baseLength * 2.0f;
			Vector3 newCenter = oldRoot.center + new Vector3(xDirection * halfLen, yDirection * halfLen, zDirection * halfLen);
			// Create a new, bigger octree root node
			var newRootNode = new PointOctreeNode<T>(this, newCenter, newLength);
			if (oldRoot.HasAnyObjects()) {
				// Create 7 new octree children to go with the old root as children of the new root
				var rootSector = OctreeUtils.GetSector(oldRoot.center - newCenter);
				var children = new PointOctreeNode<T>[OctreeUtils.SubdivideCount];
				children[(int)rootSector] = oldRoot;
				// Attach the new children to the new root node
				newRootNode.SetChildren(children);
			}
			_rootNode = newRootNode;
		}
		
		/// <summary>
		/// Shrink the octree if possible, else leave it the same.
		/// </summary>
		void ShrinkIfPossible() {
			_rootNode = _rootNode.ShrinkIfPossible(initialSize);
		}
		#endregion
		
		
		
		#region Gizmos / Debug Draw
		/// <summary>
		/// Draws node boundaries visually for debugging.
		/// Must be called from OnDrawGizmos externally. <seealso cref="DrawObjectPositionsGizmos"/>
		/// </summary>
		public void DrawNodeBoundsGizmos() {
			_rootNode.DrawNodeBoundsGizmos();
		}
		
		/// <summary>
		/// Draws the positions of all objects in the tree visually for debugging.
		/// Must be called from OnDrawGizmos externally. <seealso cref="DrawNodeBoundsGizmos"/>
		/// </summary>
		public void DrawObjectPositionsGizmos() {
			_rootNode.DrawObjectPositionsGizmos();
		}
		#endregion
	}
}
