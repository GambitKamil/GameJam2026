using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

[RequireComponent(typeof(GameManager))]
public sealed class RouteOverlay : MonoBehaviour
{
    [SerializeField]
    private bool showTemporaryInterface = true;

    private static readonly Color PanelColor = new Color32(19, 24, 32, 255);
    private static readonly Color AccentColor = new Color32(47, 91, 123, 255);

    private GameManager game;
    private Canvas canvas;
    private Font previewFont;
    private RectTransform menu;
    private RectTransform intro;
    private RectTransform finish;
    private UnityEngine.UI.Text progress;
    private UnityEngine.UI.Button skip;
    private UnityEngine.UI.Text introStatus;
    private UnityEngine.UI.Image fade;

    private void Start()
    {
        game = GetComponent<GameManager>();
        if (GameManager.Instance != game)
        {
            return;
        }

        GameObject canvasObject = new GameObject("Route Canvas", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler),
            typeof(UnityEngine.UI.GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;

        if (showTemporaryInterface)
        {
            BuildTemporaryInterface((RectTransform)canvas.transform);
            game.StateChanged += UpdatePanels;
            game.ProgressChanged += UpdateProgress;
            UpdatePanels(game.State);
        }

        fade = CreateFill("Transition Fade", (RectTransform)canvas.transform, Color.black);
        game.FadeChanged += UpdateFade;
        UpdateFade(game.FadeOpacity);
    }

    private void OnDestroy()
    {
        if (game == null)
        {
            return;
        }

        game.StateChanged -= UpdatePanels;
        game.ProgressChanged -= UpdateProgress;
        game.FadeChanged -= UpdateFade;
    }

    private void BuildTemporaryInterface(RectTransform root)
    {
        // This small preview is replaced by the design team's TMP interface.
        previewFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        menu = CreateFill("Menu", root, PanelColor).rectTransform;
        CreateLabel(menu, "Game Jam 2026", 54, 105, 660, 76);
        CreateButton(menu, "Start Game", 0, game.StartGame);

        intro = CreateFill("Intro", root, PanelColor).rectTransform;
        CreateLabel(intro, "Intro", 54, 105, 660, 76);
        introStatus = CreateLabel(intro, "", 24, 38, 680, 48);
        skip = CreateButton(intro, "Skip Intro", -38, game.SkipIntro);
        CreateButton(intro, "Back to Menu", -112, game.ReturnToMenu);

        finish = CreateFill("Finish", root, Color.black).rectTransform;
        CreateLabel(finish, "To be continued", 54, 132, 760, 80);
        progress = CreateLabel(root, "", 28, 0, 260, 52);
        progress.rectTransform.anchorMin = progress.rectTransform.anchorMax = new Vector2(0f, 1f);
        progress.rectTransform.pivot = new Vector2(0f, 1f);
        progress.rectTransform.anchoredPosition = new Vector2(24f, -20f);
        progress.alignment = TextAnchor.MiddleLeft;
        progress.color = Color.white;
        var shadow = progress.gameObject.AddComponent<UnityEngine.UI.Shadow>();
        shadow.effectColor = Color.black;
        shadow.effectDistance = new Vector2(2f, -2f);
        CreateButton(finish, "Back to Menu", -12, game.ReturnToMenu);
    }

    private void UpdatePanels(GameState state)
    {
        menu.gameObject.SetActive(state == GameState.Menu);
        intro.gameObject.SetActive(state == GameState.Intro);
        finish.gameObject.SetActive(state == GameState.Finished);
        progress.gameObject.SetActive(game.CurrentLevelIndex >= 0 && state != GameState.Menu &&
            state != GameState.Intro);

        if (state == GameState.Intro)
        {
            bool firstLevelAvailable = game.IsRouteAvailable;
            skip.interactable = firstLevelAvailable;
            introStatus.text = firstLevelAvailable ? "Intro video pending" : "Levels are not connected yet";
        }

        if ((state == GameState.Menu || state == GameState.Intro || state == GameState.Finished) &&
            EventSystem.current == null)
        {
            new GameObject("Route Event System", typeof(EventSystem), typeof(StandaloneInputModule));
        }
    }

    private void UpdateProgress(int level, int total, string sceneName)
    {
        progress.text = $"{level}/{total} {sceneName}";
        progress.gameObject.SetActive(true);
    }

    private void UpdateFade(float opacity)
    {
        fade.color = new Color(0f, 0f, 0f, opacity);
        fade.raycastTarget = opacity > 0f;
    }

    private UnityEngine.UI.Button CreateButton(RectTransform parent, string title, float y, UnityAction onClick)
    {
        var target = CreateRect(title, parent, y, 232, 54);
        var image = target.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = AccentColor;
        var button = target.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);
        CreateLabel(target, title, 24, 0, 232, 54);
        return button;
    }

    private UnityEngine.UI.Text CreateLabel(RectTransform parent, string value, int size,
        float y, float width, float height)
    {
        var rect = CreateRect(value.Length > 0 ? value + " Label" : "Status Label", parent, y, width, height);
        var label = rect.gameObject.AddComponent<UnityEngine.UI.Text>();
        label.font = previewFont;
        label.fontSize = size;
        label.text = value;
        label.color = Color.white;
        label.alignment = TextAnchor.MiddleCenter;
        label.raycastTarget = false;
        return label;
    }

    private static RectTransform CreateRect(string name, RectTransform parent, float y, float width, float height)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    private static UnityEngine.UI.Image CreateFill(string name, RectTransform parent, Color color)
    {
        var rect = CreateRect(name, parent, 0f, 0f, 0f);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }
}
