using System.Collections;
using UnityEngine;

// Corridor opening: the prisoner steps out of the cell-block door, takes a couple of steps onto the
// walking lane and the door shuts behind him. Then the level entrance hands control back.
public sealed class CorridorArrival : MonoBehaviour
{
    [SerializeField] private KitchenPassageDoor door;
    [SerializeField] private SideViewPlayer player;
    [SerializeField] private LevelEntrance entrance;

    private IEnumerator Start()
    {
        if (door != null && player != null)
            yield return door.Arrive(player);
        if (entrance != null)
            entrance.CompleteEntrance();
    }
}
