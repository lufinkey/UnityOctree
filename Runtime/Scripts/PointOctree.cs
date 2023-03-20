using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;

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
	public class PointOctree<T>
	{
		// The total amount of objects currently in the tree
		public int Count { get; private set; }

		// Root node of the octree
		PointOctreeNode<T> rootNode;

		// Size that the octree was on creation
		readonly float initialSize;

		// Minimum side length that a node can be - essentially an alternative to having a max depth
		readonly float minSize;

		/// <summary>
		/// Constructor for the point octree.
		/// </summary>
		/// <param name="initialWorldSize">Size of the sides of the initial node. The octree will never shrink smaller than this.</param>
		/// <param name="initialWorldPos">Position of the centre of the initial node.</param>
		/// <param name="minNodeSize">Nodes will stop splitting if the new nodes would be smaller than this.</param>
		public PointOctree(float initialWorldSize, Vector3 initialWorldPos, float minNodeSize)
		{
			if (minNodeSize > initialWorldSize)
			{
				Debug.LogWarning("Minimum node size must be at least as big as the initial world size. Was: " +
				                 minNodeSize + " Adjusted to: " + initialWorldSize);
				minNodeSize = initialWorldSize;
			}

			Count = 0;
			initialSize = initialWorldSize;
			minSize = minNodeSize;
			rootNode = new PointOctreeNode<T>(initialSize, minSize, initialWorldPos);
		}

		// #### PUBLIC METHODS ####

		/// <summary>
		/// Add an object.
		/// </summary>
		/// <param name="obj">Object to add.</param>
		/// <param name="objPos">Position of the object.</param>
		public void Add(T obj, Vector3 objPos)
		{
			// Add object or expand the octree until it can be added
			int count = 0; // Safety check against infinite/excessive growth
			while (!rootNode.Add(obj, objPos))
			{
				Grow(objPos - rootNode.Center);
				if (++count > 20)
				{
					Debug.LogError("Aborted Add operation as it seemed to be going on forever (" + (count - 1) +
					               ") attempts at growing the octree.");
					return;
				}
			}

			Count++;
		}

		/// <summary>
		/// Remove an object. Makes the assumption that the object only exists once in the tree.
		/// </summary>
		/// <param name="obj">Object to remove.</param>
		/// <returns>True if the object was removed successfully.</returns>
		public bool Remove(T obj)
		{
			bool removed = rootNode.Remove(obj);

			// See if we can shrink the octree down now that we've removed the item
			if (removed)
			{
				Count--;
				Shrink();
			}

			return removed;
		}

		/// <summary>
		/// Removes the specified object at the given position. Makes the assumption that the object only exists once in the tree.
		/// </summary>
		/// <param name="obj">Object to remove.</param>
		/// <param name="objPos">Position of the object.</param>
		/// <returns>True if the object was removed successfully.</returns>
		public bool Remove(T obj, Vector3 objPos)
		{
			bool removed = rootNode.Remove(obj, objPos);

			// See if we can shrink the octree down now that we've removed the item
			if (removed)
			{
				Count--;
				Shrink();
			}

			return removed;
		}

		/// <summary>
		/// Returns objects that are within <paramref name="maxDistance"/> of the specified ray.
		/// If none, returns false. Uses supplied list for results.
		/// </summary>
		/// <param name="ray">The ray to compare distance to</param>
		/// <param name="maxDistance">Maximum distance from the ray to consider</param>
		/// <param name="nearBy">Pre-initialized list to populate</param>
		/// <param name="filter">Filter objects to include (return true to include the object, false to not include it)</param>
		/// <returns>True if items are found, false if not</returns>
		public bool GetNearbyNonAlloc(in Ray ray, float maxDistance, List<T> nearBy, System.Predicate<T> filter = null)
		{
			nearBy.Clear();
			rootNode.GetNearby(ray, maxDistance, nearBy, filter);
			if (nearBy.Count > 0)
				return true;
			return false;
		}

		/// <summary>
		/// Returns objects that are within <paramref name="maxDistance"/> of the specified ray.
		/// If none, returns an empty array (not null).
		/// </summary>
		/// <param name="ray">The ray to compare distance to</param>
		/// <param name="maxDistance">Maximum distance from the ray to consider.</param>
		/// <param name="filter">Filter objects to include (return true to include the object, false to not include it)</param>
		/// <returns>Objects within range.</returns>
		public T[] GetNearby(in Ray ray, float maxDistance, System.Predicate<T> filter = null)
		{
			List<T> collidingWith = new List<T>();
			rootNode.GetNearby(ray, maxDistance, collidingWith, filter);
			return collidingWith.ToArray();
		}

		/// <summary>
		/// Returns objects that are within <paramref name="maxDistance"/> of the specified position.
		/// If none, returns an empty array (not null).
		/// </summary>
		/// <param name="position">The position to compare distances with</param>
		/// <param name="maxDistance">Maximum distance from the position to consider.</param>
		/// <param name="filter">Filter objects to include (return true to include the object, false to not include it)</param>
		/// <returns>Objects within range.</returns>
		public T[] GetNearby(in Vector3 position, float maxDistance, System.Predicate<T> filter = null)
		{
			List<T> collidingWith = new List<T>();
			rootNode.GetNearby(position, maxDistance, collidingWith, filter);
			return collidingWith.ToArray();
		}

		public void GetNearbyWithDistances(in Vector3 position, float maxDistance, List<ItemInfoWithDistance<T>> output, System.Predicate<T> filter = null)
		{
			rootNode.GetNearbyWithDistances(position, maxDistance, output, filter);
		}
		
		// A stupid temp implementation of this thing.
		public void GetNearbyN(Vector3 position, List<ItemInfoWithDistance<T>> output, int desiredCount, float startingDistance)
		{
			if (Count <= desiredCount)
				return;
			
			float maxDistance = startingDistance;
			while (true)
			{
				GetNearbyWithDistances(position, maxDistance, output);
				if (output.Count >= desiredCount)
					break;
				maxDistance += startingDistance;
				Debug.Log("Another iteration");
			}
		}
		
		// A stupid temp implementation of this thing.
		public T GetClosest(in Vector3 position, float startingDistance = float.MaxValue, List<ItemInfoWithDistance<T>> cache = null)
		{
			Assert.IsTrue(Count > 0);
			if (cache == null) {
				cache = new List<ItemInfoWithDistance<T>>();
			}
			float maxDistance = startingDistance;
			while (true)
			{
				GetNearbyWithDistances(position, maxDistance, cache);
				if (cache.Count > 0)
					break;
				maxDistance *= 2;
			}

			int minIndex = -1;
			float minDistance = float.PositiveInfinity;
			for (int i = 0; i < cache.Count; i++)
			{
				float dist = cache[i].distance;
				if (dist < minDistance)
				{
					minIndex = i;
					minDistance = dist;
				}
			}

			return cache[minIndex].obj;
		}
		
		/// <summary>
		/// Returns objects that are within <paramref name="maxDistance"/> of the specified position.
		/// If none, returns false. Uses supplied list for results.
		/// </summary>
		/// <param name="position">The position to compare distances with</param>
		/// <param name="maxDistance">Maximum distance from the position to consider</param>
		/// <param name="nearBy">Pre-initialized list to populate</param>
		/// <param name="filter">Filter objects to include (return true to include the object, false to not include it)</param>
		/// <returns>True if items are found, false if not</returns>
		public bool GetNearbyNonAlloc(in Vector3 position, float maxDistance, List<T> nearBy, System.Predicate<T> filter = null)
		{
			nearBy.Clear();
			rootNode.GetNearby(position, maxDistance, nearBy, filter);
			if (nearBy.Count > 0)
				return true;
			return false;
		}
		
		/// <summary>
		/// Finds the closest object in the octree using a given visibility and distance calculation method
		/// </summary>
		/// <param name="getPointDistance">Calculates the distance to the given point</param>
		/// <param name="isBoundsVisible">Calculates if the given bounds are visible to this search</param>
		/// <param name="filter">An optional filter method to only include certain objects</param>
		/// <param name="closestObj">The closest object found in the Octree, or `default` if not found</param>
		/// <param name="closestDistance">The square distance of the closest object to the origin (in 2D view space)</param>
		/// <returns>true if an object could be found, false otherwise</returns>
		public bool FindClosest(PointOctreeNode<T>.TryGetPointDistanceMethod getPointDistance, PointOctreeNode<T>.IsBoundsVisible isBoundsVisible, System.Predicate<T> filter, out T closestObj, out float closestDistance) {
			return rootNode.FindClosest(getPointDistance, isBoundsVisible, filter, out closestObj, out closestDistance);
		}
		
		/// <summary>
		/// Finds the closest point in the given view direction
		/// </summary>
		/// <param name="origin">A tree position to compare against</param>
		/// <param name="viewDirection">The view direction to check in</param>
		/// <param name="minDotProduct">The minimum allowed dot product between the direction from the origin to an object and the view direction</param>
		/// <param name="treeToWorld">Converts a tree position to a world position</param>
		/// <param name="camera">The camera viewing the points</param>
		/// <param name="filter">An optional filter method to only include certain objects</param>
		/// <param name="closestObj">The closest object found in the Octree, or `default` if not found</param>
		/// <param name="closestDistance">The square distance of the closest object to the origin (in 2D view space)</param>
		/// <returns>true if an object could be found, false otherwise</returns>
		public bool FindClosestInViewDirection(Vector3 origin, Vector2 viewDirection, float minDotProduct, System.Func<Vector3,Vector3> treeToWorld, Camera camera, System.Predicate<T> filter, out T closestObj, out float closestDistance) {
			var cameraViewportRect = new Rect(0, 0, 1, 1);
			viewDirection = viewDirection.normalized;
			var originInViewport = camera.WorldToViewportPoint(treeToWorld?.Invoke(origin) ?? origin);
			var originInViewport2D = new Vector2(originInViewport.x, originInViewport.y);
			PointOctreeNode<T>.TryGetPointDistanceMethod getPointDistance = (Vector3 objTreePoint, out float distance) => {
				var viewportPoint = camera.WorldToViewportPoint(treeToWorld?.Invoke(objTreePoint) ?? objTreePoint);
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
			PointOctreeNode<T>.IsBoundsVisible isBoundsVisible = (Bounds bounds) => {
				var boundsInViewport = GetBoundsViewportBounds(bounds, camera, treeToWorld);
				var viewportBoundsMin = boundsInViewport.min;
				var viewportBoundsMax = boundsInViewport.max;
				if (viewportBoundsMin.z <= 0 && viewportBoundsMax.z <= 0) {
					return false;
				}
				var rectInViewport = Rect.MinMaxRect(viewportBoundsMin.x, viewportBoundsMin.y, viewportBoundsMax.x, viewportBoundsMax.y);
				if (!cameraViewportRect.Overlaps(rectInViewport)) {
					return false;
				}
				if (rectInViewport.Contains(originInViewport2D)) {
					return true;
				}
				var closestPoint = GetClosestEdgePoint(rectInViewport, origin);
				var pointDirection2D = (closestPoint - originInViewport2D);
				float dotProduct = Vector2.Dot(viewDirection, pointDirection2D.normalized);
				if (dotProduct >= minDotProduct) {
					return true;
				}
				foreach (var point2D in GetRectPoints(rectInViewport)) {
					pointDirection2D = (point2D - originInViewport2D);
					dotProduct = Vector2.Dot(viewDirection, pointDirection2D.normalized);
					if (dotProduct >= minDotProduct) {
						return true;
					}
				}
				return false;
			};
			return FindClosest(getPointDistance, isBoundsVisible, filter, out closestObj, out closestDistance);
		}
		
		public static IEnumerable<Vector2> GetRectPoints(Rect rect) {
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
		
		private static IEnumerable<Vector3> GetBoundsCorners(Bounds bounds) {
			Vector3 min = bounds.min;
			Vector3 max = bounds.max;
			// min-all, go from min to max z
			yield return new Vector3(min.x, min.y, min.z);
			yield return new Vector3(min.x, min.y, max.z);
			yield return new Vector3(min.x, max.y, min.z);
			yield return new Vector3(min.x, max.y, max.z);
			yield return new Vector3(max.x, min.y, min.z);
			yield return new Vector3(max.x, min.y, max.z);
			yield return new Vector3(max.x, max.y, min.z);
			yield return new Vector3(max.x, max.y, max.z);
		}
		
		private static Bounds GetBoundsViewportBounds(Bounds bounds, Camera camera, System.Func<Vector3,Vector3> treeToWorld) {
			var viewportPoint = camera.WorldToViewportPoint(bounds.center);
			float minX = viewportPoint.x;
			float minY = viewportPoint.y;
			float maxX = viewportPoint.x;
			float maxY = viewportPoint.y;
			float minZ = viewportPoint.z;
			float maxZ = viewportPoint.z;
			foreach (var point in GetBoundsCorners(bounds)) {
				viewportPoint = camera.WorldToViewportPoint(treeToWorld?.Invoke(point) ?? point);
				if (viewportPoint.x < minX) {
					minX = viewportPoint.x;
				} else if (viewportPoint.x > maxX) {
					maxX = viewportPoint.x;
				}
				if (viewportPoint.y < minY) {
					minY = viewportPoint.y;
				} else if (viewportPoint.y > maxY) {
					maxY = viewportPoint.y;
				}
				if (viewportPoint.z < minZ) {
					minZ = viewportPoint.z;
				} else if (viewportPoint.z > maxZ) {
					maxZ = viewportPoint.z;
				}
			}
			var viewportBounds = new Bounds();
			viewportBounds.SetMinMax(min: new Vector3(minX, minY, minZ), max: new Vector3(maxX, maxY, maxZ));
			return viewportBounds;
		}
		
		/// <summary>
		/// Return all objects in the tree.
		/// If none, returns an empty array (not null).
		/// </summary>
		/// <returns>All objects.</returns>
		public ICollection<T> GetAll()
		{
			List<T> objects = new List<T>(Count);
			rootNode.GetAll(objects);
			return objects;
		}

		/// <summary>
		/// Draws node boundaries visually for debugging.
		/// Must be called from OnDrawGizmos externally. See also: DrawAllObjects.
		/// </summary>
		public void DrawAllBounds()
		{
			rootNode.DrawAllBounds();
		}

		/// <summary>
		/// Draws the bounds of all objects in the tree visually for debugging.
		/// Must be called from OnDrawGizmos externally. See also: DrawAllBounds.
		/// </summary>
		public void DrawAllObjects()
		{
			rootNode.DrawAllObjects();
		}

		// #### PRIVATE METHODS ####

		/// <summary>
		/// Grow the octree to fit in all objects.
		/// </summary>
		/// <param name="direction">Direction to grow.</param>
		void Grow(Vector3 direction)
		{
			int xDirection = direction.x >= 0 ? 1 : -1;
			int yDirection = direction.y >= 0 ? 1 : -1;
			int zDirection = direction.z >= 0 ? 1 : -1;
			PointOctreeNode<T> oldRoot = rootNode;
			float half = rootNode.SideLength / 2;
			float newLength = rootNode.SideLength * 2;
			Vector3 newCenter = rootNode.Center + new Vector3(xDirection * half, yDirection * half, zDirection * half);

			// Create a new, bigger octree root node
			rootNode = new PointOctreeNode<T>(newLength, minSize, newCenter);

			if (oldRoot.HasAnyObjects())
			{
				// Create 7 new octree children to go with the old root as children of the new root
				int rootPos = rootNode.BestFitChild(oldRoot.Center);
				PointOctreeNode<T>[] children = new PointOctreeNode<T>[8];
				for (int i = 0; i < 8; i++)
				{
					if (i == rootPos)
					{
						children[i] = oldRoot;
					}
					else
					{
						xDirection = i % 2 == 0 ? -1 : 1;
						yDirection = i > 3 ? -1 : 1;
						zDirection = (i < 2 || (i > 3 && i < 6)) ? -1 : 1;
						children[i] = new PointOctreeNode<T>(oldRoot.SideLength, minSize,
							newCenter + new Vector3(xDirection * half, yDirection * half, zDirection * half));
					}
				}

				// Attach the new children to the new root node
				rootNode.SetChildren(children);
			}
		}

		/// <summary>
		/// Shrink the octree if possible, else leave it the same.
		/// </summary>
		void Shrink()
		{
			rootNode = rootNode.ShrinkIfPossible(initialSize);
		}
	}
}