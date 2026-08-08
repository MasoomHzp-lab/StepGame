using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using System.IO;

[InitializeOnLoad]
public static class StepGameUIButtonKitInstaller
{
    private const string Root = "Assets/StepGameUI/ButtonKit";
    private const string TextureRoot = Root + "/Textures";
    private const string PrefabRoot = Root + "/Prefabs";

    private const string WhitePrefabPath =
        PrefabRoot + "/StepGame_WhiteButton.prefab";

    private const string BluePrefabPath =
        PrefabRoot + "/StepGame_PrimaryBlueButton.prefab";

    static StepGameUIButtonKitInstaller()
    {
        EditorApplication.delayCall += AutoInstall;
    }

    private static void AutoInstall()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        if (!File.Exists(WhitePrefabPath) || !File.Exists(BluePrefabPath))
        {
            BuildButtonKit(false);
        }
    }

    [MenuItem("Tools/StepGame UI/Rebuild Button Kit")]
    public static void RebuildFromMenu()
    {
        BuildButtonKit(true);
    }

    // ---------------------------------------------------------
    // RELIABLE UI CREATION
    // Select a UI Panel/RectTransform in Hierarchy, then use:
    // GameObject > StepGame UI > White Button
    // ---------------------------------------------------------

    [MenuItem("GameObject/StepGame UI/White Button", false, 10)]
    private static void CreateWhiteButton(MenuCommand command)
    {
        CreateButtonInstance(WhitePrefabPath, "WhiteButton");
    }

    [MenuItem("GameObject/StepGame UI/Primary Blue Button", false, 11)]
    private static void CreateBlueButton(MenuCommand command)
    {
        CreateButtonInstance(BluePrefabPath, "PrimaryBlueButton");
    }

    [MenuItem("Tools/StepGame UI/Fix Selected UI RectTransform")]
    private static void FixSelectedRectTransform()
    {
        GameObject selected = Selection.activeGameObject;

        if (selected == null)
        {
            EditorUtility.DisplayDialog(
                "StepGame UI",
                "اول یک UI Object را در Hierarchy انتخاب کن.",
                "OK"
            );
            return;
        }

        RectTransform rt = selected.GetComponent<RectTransform>();
        if (rt == null)
        {
            EditorUtility.DisplayDialog(
                "StepGame UI",
                "آبجکت انتخاب‌شده RectTransform ندارد و UI نیست.",
                "OK"
            );
            return;
        }

        Undo.RecordObject(rt, "Fix StepGame UI RectTransform");
        NormalizeRectTransform(rt, false);
        EditorUtility.SetDirty(rt);
    }

    private static void CreateButtonInstance(string prefabPath, string fallbackName)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

        if (prefab == null)
        {
            BuildButtonKit(false);
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        }

        if (prefab == null)
        {
            Debug.LogError("StepGame UI: Button prefab could not be loaded.");
            return;
        }

        Transform parent = null;

        if (Selection.activeTransform != null &&
            Selection.activeTransform.GetComponent<RectTransform>() != null)
        {
            parent = Selection.activeTransform;
        }

        GameObject instance =
            (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);

        if (instance == null)
        {
            instance = Object.Instantiate(prefab, parent);
            instance.name = fallbackName;
        }

        Undo.RegisterCreatedObjectUndo(instance, "Create StepGame UI Button");

        RectTransform rt = instance.GetComponent<RectTransform>();
        if (rt != null)
        {
            NormalizeRectTransform(rt, true);
        }

        // Make sure it is treated as UI layer.
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer >= 0)
            instance.layer = uiLayer;

        Selection.activeGameObject = instance;
        EditorGUIUtility.PingObject(instance);
    }

    private static void BuildButtonKit(bool showMessage)
    {
        EnsureFolder("Assets/StepGameUI");
        EnsureFolder(Root);
        EnsureFolder(PrefabRoot);

        ConfigureSprite(TextureRoot + "/White_Normal.png");
        ConfigureSprite(TextureRoot + "/White_Hover.png");
        ConfigureSprite(TextureRoot + "/White_Pressed.png");
        ConfigureSprite(TextureRoot + "/White_Disabled.png");

        ConfigureSprite(TextureRoot + "/PrimaryBlue_Normal.png");
        ConfigureSprite(TextureRoot + "/PrimaryBlue_Hover.png");
        ConfigureSprite(TextureRoot + "/PrimaryBlue_Pressed.png");
        ConfigureSprite(TextureRoot + "/PrimaryBlue_Disabled.png");

        AssetDatabase.Refresh();

        CreateButtonPrefab(
            "StepGame_WhiteButton",
            TextureRoot + "/White_Normal.png",
            TextureRoot + "/White_Hover.png",
            TextureRoot + "/White_Pressed.png",
            TextureRoot + "/White_Disabled.png"
        );

        CreateButtonPrefab(
            "StepGame_PrimaryBlueButton",
            TextureRoot + "/PrimaryBlue_Normal.png",
            TextureRoot + "/PrimaryBlue_Hover.png",
            TextureRoot + "/PrimaryBlue_Pressed.png",
            TextureRoot + "/PrimaryBlue_Disabled.png"
        );

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (showMessage)
        {
            EditorUtility.DisplayDialog(
                "StepGame UI",
                "Button Kit v2 rebuilt successfully.\n\n" +
                "Prefabs:\nAssets/StepGameUI/ButtonKit/Prefabs\n\n" +
                "برای ساخت مطمئن داخل Panel:\n" +
                "Panel را انتخاب کن و از GameObject > StepGame UI استفاده کن.",
                "OK"
            );
        }
    }

    private static void ConfigureSprite(string path)
    {
        TextureImporter importer =
            AssetImporter.GetAtPath(path) as TextureImporter;

        if (importer == null)
            return;

        bool changed = false;

        if (importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            changed = true;
        }

        if (importer.spriteImportMode != SpriteImportMode.Single)
        {
            importer.spriteImportMode = SpriteImportMode.Single;
            changed = true;
        }

        if (!importer.alphaIsTransparency)
        {
            importer.alphaIsTransparency = true;
            changed = true;
        }

        if (importer.mipmapEnabled)
        {
            importer.mipmapEnabled = false;
            changed = true;
        }

        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;

        // 9-slice border.
        Vector4 desiredBorder = new Vector4(54f, 54f, 54f, 54f);

        if (importer.spriteBorder != desiredBorder)
        {
            importer.spriteBorder = desiredBorder;
            changed = true;
        }

        if (changed)
            importer.SaveAndReimport();
    }

    private static void CreateButtonPrefab(
        string prefabName,
        string normalPath,
        string hoverPath,
        string pressedPath,
        string disabledPath)
    {
        Sprite normal =
            AssetDatabase.LoadAssetAtPath<Sprite>(normalPath);

        Sprite hover =
            AssetDatabase.LoadAssetAtPath<Sprite>(hoverPath);

        Sprite pressed =
            AssetDatabase.LoadAssetAtPath<Sprite>(pressedPath);

        Sprite disabled =
            AssetDatabase.LoadAssetAtPath<Sprite>(disabledPath);

        if (normal == null ||
            hover == null ||
            pressed == null ||
            disabled == null)
        {
            Debug.LogError(
                "StepGame UI: one or more button sprites could not be loaded."
            );
            return;
        }

        GameObject go = new GameObject(
            prefabName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button)
        );

        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer >= 0)
            go.layer = uiLayer;

        RectTransform rt = go.GetComponent<RectTransform>();

        // IMPORTANT:
        // These values make the prefab behave like a normal UI element
        // immediately when it becomes a child of a Canvas/Panel.
        NormalizeRectTransform(rt, true);

        Image image = go.GetComponent<Image>();
        image.sprite = normal;
        image.type = Image.Type.Sliced;
        image.preserveAspect = false;
        image.raycastTarget = true;
        image.color = Color.white;

        Button button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.SpriteSwap;

        SpriteState spriteState = new SpriteState
        {
            highlightedSprite = hover,
            pressedSprite = pressed,
            selectedSprite = hover,
            disabledSprite = disabled
        };

        button.spriteState = spriteState;

        Navigation nav = button.navigation;
        nav.mode = Navigation.Mode.Automatic;
        button.navigation = nav;

        string prefabPath =
            PrefabRoot + "/" + prefabName + ".prefab";

        PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
        Object.DestroyImmediate(go);
    }

    /// <summary>
    /// Resets the RectTransform to clean UI-local values.
    /// </summary>
    private static void NormalizeRectTransform(
        RectTransform rt,
        bool resetSize)
    {
        if (rt == null)
            return;

        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);

        rt.anchoredPosition = Vector2.zero;
        rt.localRotation = Quaternion.identity;
        rt.localScale = Vector3.one;

        Vector3 localPosition = rt.localPosition;
        localPosition.z = 0f;
        rt.localPosition = localPosition;

        if (resetSize)
            rt.sizeDelta = new Vector2(360f, 96f);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        string parent =
            Path.GetDirectoryName(path)?.Replace("\\", "/");

        string name =
            Path.GetFileName(path);

        if (!string.IsNullOrEmpty(parent) &&
            !AssetDatabase.IsValidFolder(parent))
        {
            EnsureFolder(parent);
        }

        if (!string.IsNullOrEmpty(parent))
            AssetDatabase.CreateFolder(parent, name);
    }
}
