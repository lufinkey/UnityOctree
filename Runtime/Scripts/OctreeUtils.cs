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

		public static Vector3 GetDirection(this OctreeNodeSector sector) {
			switch (sector) {
				case OctreeNodeSector.LeftDownBack: // x-, y-, z-
					return new Vector3(-1, -1, -1);
				case OctreeNodeSector.RightDownBack: // x+, y-, z-
					return new Vector3(1, -1, -1);
				case OctreeNodeSector.LeftUpBack: // x- y+, z-
					return new Vector3(-1, 1, -1);
				case OctreeNodeSector.RightUpBack: // x+, y+, z-
					return new Vector3(1, 1, -1);
				case OctreeNodeSector.LeftDownFront: // x-, y-, z+
					return new Vector3(-1, -1, 1);
				case OctreeNodeSector.RightDownFront: // x+, y-, z+
					return new Vector3(1, -1, 1);
				case OctreeNodeSector.LeftUpFront: // x-, y+, z+
					return new Vector3(-1, 1, 1);
				case OctreeNodeSector.RightUpFront: // x+, y+, z+
					return new Vector3(1, 1, 1);
			}
			Debug.LogError("Invalid sector " + sector);
			return Vector3.zero;
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
