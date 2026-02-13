using System.Collections.Generic;
using UnityEngine;

namespace RimWorld;

public class RiverNode
{
	public List<RiverNode> childNodes = new List<RiverNode>();

	public Vector3 start;

	public Vector3 end;

	public float width;
}
