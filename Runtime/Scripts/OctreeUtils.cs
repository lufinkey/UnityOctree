using System.Collections.Generic;
using UnityEngine;

namespace Octrees {
	public enum OctreeMoveResult {
		None,
		Removed,
		Moved
	}
	
	public enum OctreeAxisFlags: byte {
		X = 0b001,
		Y = 0b010,
		Z = 0b100
	}
	
	public enum OctreeNodeSector: byte {
		LeftDownBack = 0, // x-, y-, z-
		RightDownBack = OctreeAxisFlags.X, // x+, y-, z-
		LeftUpBack = OctreeAxisFlags.Y, // x- y+, z-
		RightUpBack = OctreeAxisFlags.X | OctreeAxisFlags.Y, // x+, y+, z-
		LeftDownFront = OctreeAxisFlags.Z, // x-, y-, z+
		RightDownFront = OctreeAxisFlags.X | OctreeAxisFlags.Z, // x+, y-, z+
		LeftUpFront = OctreeAxisFlags.Y | OctreeAxisFlags.Z, // x-, y+, z+
		RightUpFront = OctreeAxisFlags.X | OctreeAxisFlags.Y | OctreeAxisFlags.Z // x+, y+, z+
	}
	
	public static class OctreeUtils {
		public const int SubdivideCount = 8;
		
		public delegate Vector3 PointConverter(Vector3 point);
		
		public static OctreeNodeSector GetSector(Vector3 offsetFromCenter) {
			return (OctreeNodeSector)((offsetFromCenter.x > 0 ? (byte)OctreeAxisFlags.X : 0)
			       | (offsetFromCenter.y > 0 ? (byte)OctreeAxisFlags.Y : 0)
			       | (offsetFromCenter.z > 0 ? (byte)OctreeAxisFlags.Z : 0));
		}
		
		public static readonly IReadOnlyDictionary<OctreeNodeSector,Vector3> SectorDirections = new Dictionary<OctreeNodeSector, Vector3>() {
			{ OctreeNodeSector.LeftDownBack, new Vector3(-1, -1, -1) }, // x-, y-, z-
			{ OctreeNodeSector.RightDownBack, new Vector3(1, -1, -1) }, // x+, y-, z-
			{ OctreeNodeSector.LeftUpBack, new Vector3(-1, 1, -1) }, // x-, y+, z-
			{ OctreeNodeSector.RightUpBack, new Vector3(1, 1, -1) }, // x+, y+, z-
			{ OctreeNodeSector.LeftDownFront, new Vector3(-1, -1, 1) }, // x-, y-, z+
			{ OctreeNodeSector.RightDownFront, new Vector3(1, -1, 1) }, // x+, y-, z+
			{ OctreeNodeSector.LeftUpFront, new Vector3(-1, 1, 1) }, // x-, y+, z+
			{ OctreeNodeSector.RightUpFront, new Vector3(1, 1, 1) } // x+, y+, z+
		};
		
		public static Vector3 GetDirection(this OctreeNodeSector sector) {
			if (SectorDirections.TryGetValue(sector, out var dir)) {
				return dir;
			}
			Debug.LogError("Invalid sector " + sector);
			return Vector3.zero;
		}
		
		/// <summary>
		/// Returns the closest distance to the given ray from a given point.
		/// </summary>
		/// <param name="ray">The ray.</param>
		/// <param name="point">The point to check distance from the ray.</param>
		/// <returns>Squared distance from the point to the closest point of the ray.</returns>
		public static float SqrDistanceToRay(Ray ray, Vector3 point) {
			return Vector3.Cross(ray.direction, point - ray.origin).sqrMagnitude;
		}
		
		/// <summary>
		/// Gets all the corner points of the given bounds
		/// </summary>
		/// <param name="bounds">The bounds to get the corner points of</param>
		/// <returns>An enumerable of the corner points of the bounds</returns>
		public static IEnumerable<Vector3> GetBoundsCorners(Bounds bounds) {
			Vector3 min = bounds.min;
			Vector3 max = bounds.max;
			// min-all, go from min to max z
			yield return new Vector3(min.x, min.y, min.z); // x-, y-, z-
			yield return new Vector3(max.x, min.y, min.z); // x+, y-, z-
			yield return new Vector3(min.x, max.y, min.z); // x-, y+, z-
			yield return new Vector3(max.x, max.y, min.z); // x+, y+, z-
			yield return new Vector3(min.x, min.y, max.z); // x-, y-, z+
			yield return new Vector3(max.x, min.y, max.z); // x+, y-, z+
			yield return new Vector3(min.x, max.y, max.z); // x-, y+, z+
			yield return new Vector3(max.x, max.y, max.z); // x+, y+, z+
		}
		
		/// <summary>
		/// Converts the given bounds to a bounds in viewport space
		/// </summary>
		/// <param name="bounds">The bounds to convert to viewport space</param>
		/// <param name="camera">The camera providing the viewport</param>
		/// <param name="convertPointToWorld">If the given bounds isn't already in world space, you can provide this method to convert its points to world space</param>
		/// <returns>The converted bounds in viewport space</returns>
		public static Bounds CalculateBoundsInViewport(in Bounds bounds, Camera camera, OctreeUtils.PointConverter convertPointToWorld) {
			var viewportPoint = camera.WorldToViewportPoint(bounds.center);
			float minX = viewportPoint.x;
			float minY = viewportPoint.y;
			float maxX = viewportPoint.x;
			float maxY = viewportPoint.y;
			float minZ = viewportPoint.z;
			float maxZ = viewportPoint.z;
			foreach (var point in GetBoundsCorners(bounds)) {
				viewportPoint = camera.WorldToViewportPoint(convertPointToWorld?.Invoke(point) ?? point);
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
		
		#if UNITY_EDITOR
		public static void DebugDrawBounds(Bounds b, Color color, float duration, bool depthTest = true, PointConverter convertToWorld = null) {
			// bottom
			var p1 = new Vector3(b.min.x, b.min.y, b.min.z);
			var p2 = new Vector3(b.max.x, b.min.y, b.min.z);
			var p3 = new Vector3(b.max.x, b.min.y, b.max.z);
			var p4 = new Vector3(b.min.x, b.min.y, b.max.z);
			if (convertToWorld != null) {
				p1 = convertToWorld(p1);
				p2 = convertToWorld(p2);
				p3 = convertToWorld(p3);
				p4 = convertToWorld(p4);
			}
			
			Debug.DrawLine(p1, p2, color, duration, depthTest:depthTest);
			Debug.DrawLine(p2, p3, color, duration, depthTest:depthTest);
			Debug.DrawLine(p3, p4, color, duration, depthTest:depthTest);
			Debug.DrawLine(p4, p1, color, duration, depthTest:depthTest);
			
			// top
			var p5 = new Vector3(b.min.x, b.max.y, b.min.z);
			var p6 = new Vector3(b.max.x, b.max.y, b.min.z);
			var p7 = new Vector3(b.max.x, b.max.y, b.max.z);
			var p8 = new Vector3(b.min.x, b.max.y, b.max.z);
			if (convertToWorld != null) {
				p5 = convertToWorld(p5);
				p6 = convertToWorld(p6);
				p7 = convertToWorld(p7);
				p8 = convertToWorld(p8);
			}
			
			Debug.DrawLine(p5, p6, color, duration, depthTest:depthTest);
			Debug.DrawLine(p6, p7, color, duration, depthTest:depthTest);
			Debug.DrawLine(p7, p8, color, duration, depthTest:depthTest);
			Debug.DrawLine(p8, p5, color, duration, depthTest:depthTest);
			
			// sides
			Debug.DrawLine(p1, p5, color, duration, depthTest:depthTest);
			Debug.DrawLine(p2, p6, color, duration, depthTest:depthTest);
			Debug.DrawLine(p3, p7, color, duration, depthTest:depthTest);
			Debug.DrawLine(p4, p8, color, duration, depthTest:depthTest);
		}
		#endif
	}
}
