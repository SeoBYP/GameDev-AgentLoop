using System.Collections.Generic;
using UnityEngine;

public class ObjectPool : MonoBehaviour
{
    [SerializeField] private GameObject prefab;

    private readonly Queue<GameObject> pool = new Queue<GameObject>();

    public void Prewarm(int count)
    {
        if (prefab == null || count <= 0)
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            GameObject instance = CreateInstance();
            Release(instance);
        }
    }

    public GameObject Get()
    {
        if (prefab == null)
        {
            Debug.LogError($"{nameof(ObjectPool)} on {name} is missing a prefab reference.", this);
            return null;
        }

        GameObject instance = pool.Count > 0 ? pool.Dequeue() : CreateInstance();
        instance.SetActive(true);
        return instance;
    }

    public void Release(GameObject instance)
    {
        if (instance == null)
        {
            return;
        }

        instance.transform.SetParent(transform, false);
        instance.SetActive(false);
        pool.Enqueue(instance);
    }

    private GameObject CreateInstance()
    {
        GameObject instance = Instantiate(prefab, transform);
        instance.name = prefab.name;
        instance.SetActive(false);
        return instance;
    }
}