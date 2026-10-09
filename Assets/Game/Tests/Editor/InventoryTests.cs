using NUnit.Framework;
using UnityEngine;

public class InventoryTests
{
    private GameObject go;
    private Inventory inventory;
    private ItemDefinition cigarettes;   // стакается до 5
    private ItemDefinition monster;      // стакается до 3

    [SetUp]
    public void SetUp()
    {
        go = new GameObject("InventoryTest");
        inventory = go.AddComponent<Inventory>();
        inventory.hotbarSize = 3;
        inventory.backpackSize = 2;
        TestUtil.CallAwake(inventory);

        cigarettes = TestUtil.MakeItem("Cigarettes", 5);
        monster = TestUtil.MakeItem("Monster", 3);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(go);
        Object.DestroyImmediate(cigarettes);
        Object.DestroyImmediate(monster);
    }

    [Test]
    public void Slots_AreCreated_ForHotbarAndBackpack()
    {
        Assert.AreEqual(5, inventory.Slots.Length);
        Assert.IsTrue(inventory.Slots[0].IsEmpty);
    }

    [Test]
    public void Add_FillsStackUpToMax_ThenNextSlot()
    {
        int left = inventory.Add(cigarettes, 7, silent: true);

        Assert.AreEqual(0, left);
        Assert.AreEqual(5, inventory.Slots[0].count);
        Assert.AreEqual(2, inventory.Slots[1].count);
        Assert.AreSame(cigarettes, inventory.Slots[1].item);
    }

    [Test]
    public void Add_TopsUpExistingStackFirst()
    {
        inventory.Add(cigarettes, 2, silent: true);
        inventory.Add(monster, 1, silent: true);
        inventory.Add(cigarettes, 2, silent: true);

        Assert.AreEqual(4, inventory.Slots[0].count, "вторые сигареты должны лечь в ту же стопку");
        Assert.AreSame(monster, inventory.Slots[1].item);
        Assert.IsTrue(inventory.Slots[2].IsEmpty);
    }

    [Test]
    public void Add_TopsUpOnlyToMax_RestGoesToNewSlot()
    {
        inventory.Add(cigarettes, 4, silent: true);
        inventory.Add(cigarettes, 3, silent: true);

        Assert.AreEqual(5, inventory.Slots[0].count, "стопка не может быть больше maxStack");
        Assert.AreEqual(2, inventory.Slots[1].count);
    }

    [Test]
    public void Add_WhenFull_ReturnsLeftover()
    {
        int left = inventory.Add(monster, 20, silent: true); // 5 слотов x 3 = 15

        Assert.AreEqual(5, left);
        foreach (InventorySlot s in inventory.Slots)
            Assert.AreEqual(3, s.count);
    }

    [Test]
    public void Add_ZeroOrNull_DoesNothing()
    {
        Assert.AreEqual(0, inventory.Add(cigarettes, 0, silent: true));
        Assert.AreEqual(3, inventory.Add(null, 3, silent: true));
        Assert.IsTrue(inventory.Slots[0].IsEmpty);
    }

    [Test]
    public void Swap_DifferentItems_ExchangesSlots()
    {
        inventory.Add(cigarettes, 1, silent: true);
        inventory.Add(monster, 2, silent: true);

        inventory.Swap(0, 1);

        Assert.AreSame(monster, inventory.Slots[0].item);
        Assert.AreEqual(2, inventory.Slots[0].count);
        Assert.AreSame(cigarettes, inventory.Slots[1].item);
        Assert.AreEqual(1, inventory.Slots[1].count);
    }

    [Test]
    public void Swap_SameStackable_MergesUpToMax()
    {
        inventory.Slots[0].item = cigarettes; inventory.Slots[0].count = 4;
        inventory.Slots[1].item = cigarettes; inventory.Slots[1].count = 3;

        inventory.Swap(0, 1);

        Assert.AreEqual(5, inventory.Slots[1].count, "в целевую стопку влезает только до maxStack");
        Assert.AreEqual(2, inventory.Slots[0].count);
    }

    [Test]
    public void Swap_IntoEmptySlot_MovesItem()
    {
        inventory.Add(monster, 1, silent: true);

        inventory.Swap(0, 4); // из хотбара в рюкзак

        Assert.IsTrue(inventory.Slots[0].IsEmpty);
        Assert.AreSame(monster, inventory.Slots[4].item);
    }

    [Test]
    public void CompleteUse_RemovesOneItem_AndClearsEmptySlot()
    {
        inventory.Add(cigarettes, 2, silent: true);

        inventory.CompleteUse(cigarettes, 0);
        Assert.AreEqual(1, inventory.Slots[0].count);

        inventory.CompleteUse(cigarettes, 0);
        Assert.IsTrue(inventory.Slots[0].IsEmpty);
    }

    [Test]
    public void CompleteUse_ItemMovedToAnotherSlot_StillConsumed()
    {
        inventory.Add(cigarettes, 1, silent: true);
        inventory.Swap(0, 3); // пока "курили", перетащили пачку в рюкзак

        inventory.CompleteUse(cigarettes, 0);

        Assert.IsTrue(inventory.Slots[3].IsEmpty);
    }
}
