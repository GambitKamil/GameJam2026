using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Adds a cell-block door near the start of the corridor so the level opens with the prisoner
// stepping out of it (same animation as the corridor -> kitchen door). Safe to run again.
public static class CorridorEntrancePass
{
    private const string CorridorScene = "Assets/_Game/Scenes/Sandbox/Corridor.unity";
    private const string DoorName = "Cell block door";
    private const float DoorX = -1.2f;
    private const float HalfOpening = 0.78f;          // outer edge of the jambs
    private const float ArrivalX = -0.35f;            // where control comes back: about two steps out
    private const float DoorTop = 2.31f;               // hidden behind the lintel, like the kitchen door infill

    [MenuItem("Game Jam/Corridor entrance pass")]
    public static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlaying)
            throw new InvalidOperationException("Stop Play Mode first.");
        var scene = EditorSceneManager.OpenScene(CorridorScene, OpenSceneMode.Single);

        var kitchenDoor = GameObject.Find("Kitchen door");
        if (kitchenDoor == null) throw new InvalidOperationException("Kitchen door not found.");
        var player = Object.FindFirstObjectByType<SideViewPlayer>();

        var old = GameObject.Find(DoorName);
        if (old != null) Object.DestroyImmediate(old);

        // Same door and service passage as the kitchen door, set into the back wall near the start.
        var door = Object.Instantiate(kitchenDoor, kitchenDoor.transform.parent);
        door.name = DoorName;
        door.transform.position = new Vector3(DoorX, kitchenDoor.transform.position.y, kitchenDoor.transform.position.z);
        foreach (Transform t in door.GetComponentsInChildren<Transform>(true).ToArray())
        {
            if (t == null) continue;
            if (t.name.StartsWith("Glazed kitchen tile") || t.name == "Counter edge")
                Object.DestroyImmediate(t.gameObject);
        }
        var lamp = door.GetComponentsInChildren<Light>(true).FirstOrDefault();
        if (lamp != null)
        {
            lamp.color = new Color(0.6f, 0.7f, 0.85f);
            lamp.intensity *= 0.6f;
        }

        // The prisoner ends the walk on the lane, a couple of steps right of the door.
        Vector3 lane = player.transform.position;
        var approach = door.transform.Find("Door approach");
        approach.position = new Vector3(ArrivalX, lane.y, lane.z);
        Undo.RecordObject(player.transform, "Move player start");
        player.transform.position = approach.position;

        var entrance = door.AddComponent<LevelEntrance>();
        var arrival = door.AddComponent<CorridorArrival>();
        var data = new SerializedObject(arrival);
        data.FindProperty("door").objectReferenceValue = door.GetComponent<KitchenPassageDoor>();
        data.FindProperty("player").objectReferenceValue = player;
        data.FindProperty("entrance").objectReferenceValue = entrance;
        data.ApplyModifiedPropertiesWithoutUndo();

        CutOpening();
        FillAbove();
        Label(door.transform);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return "Corridor entrance door added.";
    }

    // Split the long back-wall pieces so the doorway shows the passage behind it.
    private static void CutOpening()
    {
        foreach (var stale in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t => t.name == "Back wall above cell door").ToArray())
            Object.DestroyImmediate(stale.gameObject);
        float left = DoorX - HalfOpening, right = DoorX + HalfOpening;
        foreach (string name in new[] { "Back wall", "Painted wainscot", "Kick plate", "Dado cap" })
        {
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r => r.name == name).ToArray())
            {
                Bounds b = r.bounds;
                if (b.min.x >= right || b.max.x <= left || r.transform.rotation != Quaternion.identity) continue;
                if (b.min.y >= DoorTop - 0.01f) continue;
                var t = r.transform;
                float worldWidth = b.size.x;
                float perUnit = t.localScale.x / worldWidth;
                // Right piece: a copy from the opening to the old right end.
                if (b.max.x > right)
                {
                    var copy = Object.Instantiate(r.gameObject, t.parent);
                    copy.name = name;
                    Resize(copy.transform, right, b.max.x, perUnit);
                }
                // Left piece: this object, from the old left end to the opening.
                if (b.min.x < left) Resize(t, b.min.x, left, perUnit);
                else Object.DestroyImmediate(r.gameObject);
            }
        }
    }

    // Wall above the door (the opening is only door height), built from the wall piece left of it.
    private static void FillAbove()
    {
        float left = DoorX - HalfOpening, right = DoorX + HalfOpening;
        var template = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
            .FirstOrDefault(r => r.name == "Back wall" && Mathf.Abs(r.bounds.max.x - left) < 0.05f);
        if (template == null) return;
        Bounds b = template.bounds;
        var above = Object.Instantiate(template.gameObject, template.transform.parent);
        above.name = "Back wall above cell door";
        Resize(above.transform, left, right, template.transform.localScale.x / b.size.x);
        Vector3 scale = above.transform.localScale;
        scale.y *= (b.max.y - DoorTop) / b.size.y;
        above.transform.localScale = scale;
        Vector3 p = above.transform.position;
        p.y = (DoorTop + b.max.y) * 0.5f;
        above.transform.position = p;
    }

    private static void Resize(Transform t, float from, float to, float perUnit)
    {
        Vector3 scale = t.localScale;
        scale.x = (to - from) * perUnit;
        t.localScale = scale;
        Vector3 p = t.position;
        p.x = (from + to) * 0.5f;
        t.position = p;
    }

    private static void Label(Transform door)
    {
        var sign = new GameObject("Cell block sign");
        sign.transform.SetParent(door, false);
        sign.transform.localPosition = new Vector3(0f, 2.62f, -0.2f);
        var text = sign.AddComponent<TextMesh>();
        text.text = "CELL BLOCK  B";
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.font = font;
        text.fontSize = 64;
        text.characterSize = 0.035f;
        text.anchor = TextAnchor.MiddleCenter;
        text.color = new Color(0.55f, 0.6f, 0.66f, 0.35f);
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Art/KitchenVentilation/Vent world text.mat");
        sign.GetComponent<MeshRenderer>().sharedMaterial = material != null ? material : font.material;
    }
}
