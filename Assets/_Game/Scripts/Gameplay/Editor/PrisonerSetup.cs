using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// Imports and wires the Blender Prisoner. Every step is idempotent: running it again only re-applies settings.
public static class PrisonerSetup
{
    const string Root = "Assets/_Game/Art/Characters/Prisoner";
    const string ModelPath = Root + "/Prisoner_Final.fbx";
    const string TextureDir = Root + "/Textures";
    const string AnimationDir = Root + "/Animations";
    const string MaterialPath = Root + "/M_Prisoner.mat";
    const string ControllerDir = "Assets/_Game/Animations/Prisoner";
    const string ControllerPath = ControllerDir + "/Prisoner.controller";
    const string ModelObjectName = "Prisoner Model";

    static readonly HashSet<string> LoopingClips = new HashSet<string>
        { "Idle", "Walk", "CrouchIdle", "SneakWalk", "PullRod", "PushCart" };

    [MenuItem("Tools/Prisoner/Setup")]
    public static void Setup()
    {
        if (!File.Exists(ModelPath))
        {
            Debug.LogError($"Prisoner setup: {ModelPath} is missing.");
            return;
        }

        ConfigureTextures();
        Material material = ConfigureMaterial();
        ConfigureModel(material);
        ConfigureAnimationFiles();
        BuildController();
        AssetDatabase.SaveAssets();
        Debug.Log("Prisoner setup: model, clips, material and Animator Controller are configured.");
    }

    [MenuItem("Tools/Prisoner/Install On Player In Open Scene")]
    public static void InstallOnPlayer()
    {
        var motion = Object.FindFirstObjectByType<CellMotion>();
        if (motion == null)
        {
            Debug.LogError("Prisoner install: no CellMotion in the open scene.");
            return;
        }

        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (controller == null || modelAsset == null)
        {
            Debug.LogError("Prisoner install: run Tools/Prisoner/Setup first.");
            return;
        }

        var cellMotion = new SerializedObject(motion);
        var figure = cellMotion.FindProperty("figure").objectReferenceValue as Transform;
        Transform playerRoot = motion.transform;

        Transform model = playerRoot.Find(ModelObjectName);
        if (model == null)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, playerRoot);
            instance.name = ModelObjectName;
            model = instance.transform;
            Undo.RegisterCreatedObjectUndo(instance, "Install Prisoner");
        }

        Undo.RecordObject(model, "Install Prisoner");
        // Feet on the same floor point as the procedural figure.
        model.localPosition = figure != null ? new Vector3(0f, figure.localPosition.y, 0f) : Vector3.zero;
        model.localRotation = Quaternion.Euler(0f, 90f, 0f);
        model.localScale = Vector3.one;

        var animator = model.GetComponent<Animator>();
        if (animator == null)
            animator = Undo.AddComponent<Animator>(model.gameObject);
        Undo.RecordObject(animator, "Install Prisoner");
        animator.runtimeAnimatorController = controller;
        animator.avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        var bridge = model.GetComponent<PrisonerAnimatorBridge>();
        if (bridge == null)
            bridge = Undo.AddComponent<PrisonerAnimatorBridge>(model.gameObject);
        var legacy = new List<Transform>();
        if (figure != null)
            legacy.Add(figure);
        Transform placeholder = playerRoot.Find("Visual");
        if (placeholder != null)
            legacy.Add(placeholder);
        var bridgeData = new SerializedObject(bridge);
        SerializedProperty visuals = bridgeData.FindProperty("legacyVisuals");
        visuals.arraySize = legacy.Count;
        for (int i = 0; i < legacy.Count; i++)
            visuals.GetArrayElementAtIndex(i).objectReferenceValue = legacy[i];
        bridgeData.ApplyModifiedProperties();

        foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Undo.RecordObject(renderer, "Install Prisoner");
            renderer.updateWhenOffscreen = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        EditorSceneManager.MarkSceneDirty(playerRoot.gameObject.scene);
        Selection.activeObject = model.gameObject;
        Debug.Log($"Prisoner install: '{ModelObjectName}' is under '{playerRoot.name}'. Save the scene to keep it.");
    }

    // Batch entry: Unity.exe -batchmode -projectPath . -executeMethod PrisonerSetup.BatchInstallAndPreview
    // Optional env PRISONER_PREVIEW_DIR: where the preview images are written (outside Assets).
    public static void BatchInstallAndPreview()
    {
        Setup();
        string[] scenes = (System.Environment.GetEnvironmentVariable("PRISONER_SCENES") ?? "Assets/_Game/Scenes/Levels/Cell.unity")
            .Split(';');
        string outDir = System.Environment.GetEnvironmentVariable("PRISONER_PREVIEW_DIR");
        foreach (string scenePath in scenes)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            if (Object.FindFirstObjectByType<CellMotion>() == null)
            {
                Debug.Log($"Prisoner batch: {scenePath} has no CellMotion player, skipped.");
                continue;
            }

            InstallOnPlayer();
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"Prisoner batch: installed and saved {scenePath}.");
            if (!string.IsNullOrEmpty(outDir))
                RenderPreviews(outDir, Path.GetFileNameWithoutExtension(scenePath));
        }
    }

    // Edit-mode preview: legacy figure hidden, model sampled in its Idle pose, rendered from the level camera.
    static void RenderPreviews(string outDir, string label)
    {
        Directory.CreateDirectory(outDir);
        var bridge = Object.FindFirstObjectByType<PrisonerAnimatorBridge>();
        if (bridge == null)
            return;

        var legacy = new SerializedObject(bridge).FindProperty("legacyVisuals");
        for (int i = 0; i < legacy.arraySize; i++)
            if (legacy.GetArrayElementAtIndex(i).objectReferenceValue is Transform root)
                foreach (Renderer part in root.GetComponentsInChildren<Renderer>(true))
                    part.forceRenderingOff = true;

        GameObject model = bridge.gameObject;
        AnimationClip idle = Clip("Idle");
        if (idle != null)
        {
            AnimationMode.StartAnimationMode();
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(model, idle, 0.6f);
            AnimationMode.EndSampling();
        }

        Camera level = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
        if (level != null)
            Shoot(level, Path.Combine(outDir, label + "_GameCamera.png"), 1920, 1080);

        // Close-up from the same side as the game camera.
        var close = new GameObject("Prisoner Preview Camera").AddComponent<Camera>();
        if (level != null)
            close.CopyFrom(level);
        Vector3 chest = model.transform.position + Vector3.up * 1.15f;
        close.transform.position = chest + new Vector3(0.9f, 0.15f, -3.4f);
        close.transform.LookAt(chest);
        close.fieldOfView = 30f;
        Shoot(close, Path.Combine(outDir, label + "_CloseUp.png"), 1080, 1350);
        Object.DestroyImmediate(close.gameObject);

        if (AnimationMode.InAnimationMode())
            AnimationMode.StopAnimationMode();
    }

    static void Shoot(Camera camera, string file, int width, int height)
    {
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        image.Apply();
        File.WriteAllBytes(file, image.EncodeToPNG());
        camera.targetTexture = previousTarget;
        RenderTexture.active = previousActive;
        Object.DestroyImmediate(image);
        target.Release();
        Object.DestroyImmediate(target);
        Debug.Log($"Prisoner batch: preview written to {file}");
    }

    static void ConfigureTextures()
    {
        foreach (string path in Directory.GetFiles(TextureDir, "*.png"))
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
            if (importer == null)
                continue;
            bool linear = path.Contains("MetallicSmoothness") || path.Contains("Roughness");
            importer.sRGBTexture = !linear;
            importer.maxTextureSize = 2048;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.SaveAndReimport();
        }
    }

    static Material ConfigureMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Standard")) { name = "M_Prisoner" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }

        material.shader = Shader.Find("Standard");
        material.SetFloat("_Mode", 0f);
        material.SetOverrideTag("RenderType", "Opaque");
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        material.SetInt("_ZWrite", 1);
        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = -1;
        material.color = Color.white;
        material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(TextureDir + "/T_Prisoner_Albedo.png"));
        material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TextureDir + "/T_Prisoner_MetallicSmoothness.png"));
        material.EnableKeyword("_METALLICGLOSSMAP");
        material.SetFloat("_SmoothnessTextureChannel", 0f); // 0 = Metallic Alpha
        material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
        material.SetFloat("_GlossMapScale", 0.8f);
        material.SetTexture("_BumpMap", null);
        material.DisableKeyword("_EMISSION");
        EditorUtility.SetDirty(material);
        return material;
    }

    static void ConfigureModel(Material material)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        importer.globalScale = 1f;
        importer.useFileScale = true;
        importer.importBlendShapes = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importNormals = ModelImporterNormals.Import;
        importer.meshCompression = ModelImporterMeshCompression.Off;
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_Prisoner"), material);
        importer.importAnimation = true;
        importer.clipAnimations = ConfigureClips(importer.defaultClipAnimations);
        importer.SaveAndReimport();
    }

    static void ConfigureAnimationFiles()
    {
        Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
        if (avatar == null)
        {
            Debug.LogError("Prisoner setup: the model has no Avatar; check Configure Avatar on Prisoner_Final.fbx.");
            return;
        }

        foreach (string file in Directory.GetFiles(AnimationDir, "Prisoner@*.fbx"))
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(file.Replace('\\', '/'));
            if (importer == null)
                continue;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.animationType = ModelImporterAnimationType.Human;
            // These files hold the skeleton without the mesh, so a copied avatar reports a hierarchy
            // mismatch. Each file builds its own avatar from the same T-pose skeleton; humanoid clips
            // retarget onto Prisoner_Final through muscle space either way.
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = null;
            importer.importAnimation = true;
            importer.clipAnimations = ConfigureClips(importer.defaultClipAnimations);
            importer.SaveAndReimport();
        }
    }

    static ModelImporterClipAnimation[] ConfigureClips(ModelImporterClipAnimation[] clips)
    {
        foreach (var clip in clips)
        {
            string name = ClipName(clip.name);
            clip.name = name;
            bool loop = LoopingClips.Contains(name);
            clip.loopTime = loop;
            clip.loopPose = loop;
            clip.lockRootRotation = true;          // Bake Into Pose
            clip.keepOriginalOrientation = false;  // Based Upon: Body Orientation
            clip.lockRootHeightY = true;
            clip.keepOriginalPositionY = true;     // Based Upon: Original
            clip.heightFromFeet = false;
            clip.lockRootPositionXZ = true;
            clip.keepOriginalPositionXZ = false;   // Based Upon: Center of Mass
        }

        return clips;
    }

    static string ClipName(string take)
    {
        int bar = take.LastIndexOf('|');
        return bar >= 0 ? take.Substring(bar + 1) : take;
    }

    static AnimationClip Clip(string name)
    {
        return AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
            .FirstOrDefault(c => c.name == name && !c.name.StartsWith("__preview__"));
    }

    static void BuildController()
    {
        if (!AssetDatabase.IsValidFolder(ControllerDir))
        {
            Directory.CreateDirectory(ControllerDir);
            AssetDatabase.Refresh();
        }

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
            ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        // Already built: keep it untouched so re-running Setup does not churn the asset in git.
        string[] required = { "Idle", "Walk", "CrouchIdle", "SneakWalk" };
        ChildAnimatorState[] existing = controller.layers[0].stateMachine.states;
        if (controller.parameters.Length == 4 && required.All(name =>
                existing.Any(s => s.state.name == name && s.state.motion != null)))
            return;

        foreach (var parameter in controller.parameters)
            controller.RemoveParameter(parameter);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("WalkMul", AnimatorControllerParameterType.Float);
        controller.AddParameter("SneakMul", AnimatorControllerParameterType.Float);
        controller.AddParameter("Crouch", AnimatorControllerParameterType.Bool);
        AnimatorControllerParameter[] parameters = controller.parameters;
        foreach (var parameter in parameters)
            if (parameter.type == AnimatorControllerParameterType.Float)
                parameter.defaultFloat = parameter.name == "Speed" ? 0f : 1f;
        controller.parameters = parameters;

        AnimatorControllerLayer[] layers = controller.layers;
        layers[0].iKPass = true;
        controller.layers = layers;
        AnimatorStateMachine machine = layers[0].stateMachine;
        foreach (var transition in machine.anyStateTransitions)
            machine.RemoveAnyStateTransition(transition);
        foreach (var child in machine.states)
            machine.RemoveState(child.state);

        AnimatorState idle = State(machine, "Idle", new Vector3(260f, 0f));
        AnimatorState walk = State(machine, "Walk", new Vector3(520f, 0f), "WalkMul");
        AnimatorState crouchIdle = State(machine, "CrouchIdle", new Vector3(260f, 160f));
        AnimatorState sneak = State(machine, "SneakWalk", new Vector3(520f, 160f), "SneakMul");
        machine.defaultState = idle;

        Link(idle, walk, 0.15f, Speed(true));
        Link(walk, idle, 0.15f, Speed(false));
        Link(idle, crouchIdle, 0.25f, Crouch(true));
        Link(crouchIdle, idle, 0.25f, Crouch(false));
        Link(crouchIdle, sneak, 0.15f, Speed(true));
        Link(sneak, crouchIdle, 0.15f, Speed(false));
        Link(walk, sneak, 0.25f, Crouch(true));
        Link(sneak, walk, 0.25f, Crouch(false));

        EditorUtility.SetDirty(controller);
    }

    static AnimatorState State(AnimatorStateMachine machine, string clip, Vector3 position, string multiplier = null)
    {
        AnimatorState state = machine.AddState(clip, position);
        state.motion = Clip(clip);
        state.iKOnFeet = true;
        state.writeDefaultValues = true;
        if (multiplier != null)
        {
            state.speedParameterActive = true;
            state.speedParameter = multiplier;
        }
        if (state.motion == null)
            Debug.LogWarning($"Prisoner setup: clip '{clip}' was not found in {ModelPath}.");
        return state;
    }

    static (AnimatorConditionMode, float, string) Speed(bool moving) =>
        (moving ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less, 0.05f, "Speed");

    static (AnimatorConditionMode, float, string) Crouch(bool on) =>
        (on ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, "Crouch");

    static void Link(AnimatorState from, AnimatorState to, float duration, (AnimatorConditionMode mode, float threshold, string parameter) condition)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.duration = duration;
        transition.hasFixedDuration = true;
        transition.AddCondition(condition.mode, condition.threshold, condition.parameter);
    }
}
