using System.Collections.Generic;
using UnityEngine;

public class ItemInventory : MonoBehaviour
{
    [SerializeField] private int maxSlots = 4;

    private List<string> items;

    public int Count => items != null ? items.Count : 0;

    public int MaxSlots => maxSlots;

    private void Awake()
    {
        if (maxSlots < 1)
        {
            maxSlots = 1;
        }

        items = new List<string>(maxSlots);
    }

    public bool Add(string itemName)
    {
        if (items == null)
        {
            items = new List<string>(maxSlots);
        }

        if (string.IsNullOrEmpty(itemName) || items.Count >= maxSlots)
        {
            return false;
        }

        items.Add(itemName);
        return true;
    }

    public bool Remove(string itemName)
    {
        if (items == null || string.IsNullOrEmpty(itemName))
        {
            return false;
        }

        return items.Remove(itemName);
    }
}