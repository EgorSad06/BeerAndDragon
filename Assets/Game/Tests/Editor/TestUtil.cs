using System.Reflection;
using NUnit.Framework;
using UnityEngine;

// Хелперы для EditMode-тестов: в режиме редактора Unity не вызывает Awake у обычных
// MonoBehaviour, поэтому вызываем его вручную.
public static class TestUtil
{
    public static void CallAwake(MonoBehaviour component)
    {
        MethodInfo awake = component.GetType().GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        awake?.Invoke(component, null);
    }

    public static void AreClose(Vector3 expected, Vector3 actual, float tolerance = 0.0001f)
    {
        Assert.LessOrEqual(Vector3.Distance(expected, actual), tolerance, $"ожидалось {expected}, получено {actual}");
    }

    public static ItemDefinition MakeItem(string name, int maxStack,
        ItemDefinition.ItemKind kind = ItemDefinition.ItemKind.Consumable)
    {
        ItemDefinition item = ScriptableObject.CreateInstance<ItemDefinition>();
        item.name = name;
        item.displayName = name;
        item.maxStack = maxStack;
        item.kind = kind;
        item.useStyle = ItemDefinition.UseStyle.Instant;
        return item;
    }
}
