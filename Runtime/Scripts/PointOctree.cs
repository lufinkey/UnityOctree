﻿using System.Collections.Generic;
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
		/// Finds the object in the octree that lies in the given direction and has the closest 2D distance (in terms of the given viewing angle) to the given origin.
		/// </summary>
		/// <param name="origin">The origin point that the 2D distance will be compared against</param>
		/// <param name="direction2D">The 2D direction that the found object should lie in</param>
		/// <param name="minDotProduct2D">The minimum dot product allowed from an object's offset from the given ray origin (in 2D)</param>
		/// <param name="worldToView"> Converts a world offset to a view offset</param>
		/// <param name="nearClippingPlane">Ensure any found points are in front of this plane</param>
		/// <param name="filter">Filter function to allow or ignore certain objects in the Octree</param>
		/// <param name="closestObj">The closest object found in the Octree, or `default` if not found</param>
		/// <param name="closestSqrDistance2D">The square distance of the closest object to the origin (in 2D view space)</param>
		/// <returns>true if an object could be found, false otherwise</returns>
		public bool FindClosestInDirection2D(Vector3 origin, Vector2 direction2D, float minDotProduct2D, Quaternion worldToView, Plane? nearClippingPlane, System.Predicate<T> filter, out T closestObj, out float closestSqrDistance2D) {
			return rootNode.FindClosestInDirection2D(origin, direction2D.normalized, minDotProduct2D, worldToView, nearClippingPlane, filter, out closestObj, out closestSqrDistance2D);
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