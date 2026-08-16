using System.Collections.Generic;
using UnityEngine;

public class TileField : MonoBehaviour
{
    [SerializeField] private int _tilesPerSide = 4;

    private readonly List<GameObject> _spawned = new List<GameObject>(16);

    public int SpawnedCount => _spawned.Count;

    public void Build()
    {
        for (int x = 0; x < _tilesPerSide; x++)
        {
            for (int z = 0; z < _tilesPerSide; z++)
            {
                var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tile.transform.SetParent(transform, false);
                tile.transform.localPosition = new Vector3(x, 0f, z);
                _spawned.Add(tile);
            }
        }
    }
}