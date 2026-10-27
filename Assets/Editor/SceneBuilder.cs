#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace InspectionDemo.EditorTools
{
    /// <summary>
    /// 一键生成完整巡检场景（场景物体 + 灯光 + 玩家 + UI + 全部组件引用）。
    ///
    /// 为什么用脚本建场景而不是手工拖拽：
    ///   1. 场景可以随时重建，改布局不用记着拖了哪些引用；
    ///   2. 所有引用在代码里显式赋值，评审时可以逐行核对，不存在「Inspector 里少拖了一个」；
    ///   3. 配上 VerifyReferences()，引用完整性能被程序化验证，不靠肉眼。
    /// </summary>
    public static class SceneBuilder
    {
        const string ScenePath = "Assets/Scenes/Inspection.unity";
        const string MaterialDir = "Assets/Materials";
        const string FontPath = "Assets/Fonts/SimHei.ttf";

        static readonly Color FloorColor = new Color(0.24f, 0.26f, 0.29f);
        static readonly Color WallColor = new Color(0.66f, 0.68f, 0.70f);
        static readonly Color CabinetColor = new Color(0.28f, 0.32f, 0.38f);
        static readonly Color PipeColor = new Color(0.72f, 0.55f, 0.16f);
        static readonly Color AccentColor = new Color(0.16f, 0.42f, 0.66f);

        static readonly Color PanelBg = new Color(0.09f, 0.11f, 0.14f, 0.94f);
        static readonly Color TextMain = new Color(0.96f, 0.97f, 0.98f);
        static readonly Color TextMuted = new Color(0.68f, 0.72f, 0.77f);
        static readonly Color PrimaryButton = new Color(0.13f, 0.45f, 0.78f);
        static readonly Color NeutralButton = new Color(0.32f, 0.35f, 0.40f);

        [MenuItem("巡检工具/生成巡检场景（会覆盖现有场景）", false, 10)]
        public static void BuildScene()
        {
            Font font = LoadFont();
            if (font == null)
            {
                Debug.LogError("[Builder] 找不到中文字体，UI 文字会显示为方块。请确认 " + FontPath + " 存在。");
            }

            // 先保存当前场景，避免 NewScene 弹出「是否保存」对话框卡住自动化流程
            EditorSceneManager.SaveOpenScenes();

            EnsureFolder("Assets/Scenes");
            EnsureFolder(MaterialDir);

            Material floorMat = GetOrCreateMaterial("Floor", FloorColor);
            Material wallMat = GetOrCreateMaterial("Wall", WallColor);
            Material cabinetMat = GetOrCreateMaterial("Cabinet", CabinetColor);
            Material pipeMat = GetOrCreateMaterial("Pipe", PipeColor);
            Material accentMat = GetOrCreateMaterial("Accent", AccentColor);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            ConfigureLighting();
            BuildEnvironment(floorMat, wallMat, cabinetMat, pipeMat, accentMat);
            Camera cam = BuildPlayer();
            GameObject gameManager = BuildManagers(cam);

            Text counterText;
            GameObject canvasGo = BuildUI(font, out counterText);

            // ---- 把运行时组件的引用接起来 ----
            ApiClient api = gameManager.GetComponent<ApiClient>();
            MarkerSpawner spawner = gameManager.GetComponent<MarkerSpawner>();
            MarkerRestorer restorer = gameManager.GetComponent<MarkerRestorer>();
            InspectionUI ui = canvasGo.GetComponent<InspectionUI>();

            spawner.viewCamera = cam;

            restorer.api = api;
            restorer.spawner = spawner;
            restorer.ui = ui;
            restorer.counterText = counterText;

            ui.api = api;
            ui.spawner = spawner;
            ui.restorer = restorer;

            // ---- 保存 ----
            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
            if (!saved)
            {
                Debug.LogError("[Builder] 保存场景失败: " + ScenePath);
                return;
            }

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();

            Debug.Log("[Builder] 场景已生成并保存: " + ScenePath);
            VerifyReferences();
        }

        [MenuItem("巡检工具/验证场景引用完整性", false, 11)]
        public static void VerifyReferences()
        {
            int problems = 0;

            Camera cam = Object.FindFirstObjectByType<Camera>();
            if (cam == null)
            {
                Debug.LogError("[Verify] 场景里没有相机");
                problems++;
            }

            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                Debug.LogError("[Verify] 场景里没有 EventSystem，UI 点击会失效");
                problems++;
            }

            MarkerSpawner spawner = Object.FindFirstObjectByType<MarkerSpawner>();
            if (spawner == null)
            {
                Debug.LogError("[Verify] 找不到 MarkerSpawner");
                problems++;
            }
            else if (spawner.viewCamera == null)
            {
                Debug.LogError("[Verify] MarkerSpawner.viewCamera 未绑定，射线不会发射");
                problems++;
            }

            ApiClient api = Object.FindFirstObjectByType<ApiClient>();
            if (api == null)
            {
                Debug.LogError("[Verify] 找不到 ApiClient");
                problems++;
            }

            InspectionUI ui = Object.FindFirstObjectByType<InspectionUI>();
            if (ui == null)
            {
                Debug.LogError("[Verify] 找不到 InspectionUI");
                problems++;
            }
            else
            {
                if (ui.titleInput == null || ui.descInput == null || ui.formPanel == null)
                {
                    Debug.LogError("[Verify] InspectionUI 的表单引用未绑定");
                    problems++;
                }
                if (ui.submitButton == null || ui.cancelButton == null)
                {
                    Debug.LogError("[Verify] InspectionUI 的按钮引用未绑定");
                    problems++;
                }
                if (ui.priorityButtons == null || ui.priorityButtons.Length != 3)
                {
                    Debug.LogError("[Verify] 优先级按钮数量应为 3");
                    problems++;
                }
                if (ui.api == null || ui.spawner == null)
                {
                    Debug.LogError("[Verify] InspectionUI 未绑定 api / spawner");
                    problems++;
                }
            }

            MarkerRestorer restorer = Object.FindFirstObjectByType<MarkerRestorer>();
            if (restorer == null || restorer.api == null || restorer.spawner == null)
            {
                Debug.LogError("[Verify] MarkerRestorer 引用不完整");
                problems++;
            }

            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                Debug.LogError("[Verify] 找不到 Canvas");
                problems++;
            }
            else if (canvas.GetComponent<GraphicRaycaster>() == null)
            {
                Debug.LogError("[Verify] Canvas 缺少 GraphicRaycaster，UI 点击不会命中");
                problems++;
            }

            PlayerController player = Object.FindFirstObjectByType<PlayerController>();
            if (player == null || player.cameraPivot == null)
            {
                Debug.LogError("[Verify] PlayerController 或 cameraPivot 未绑定");
                problems++;
            }

            if (FontPathMissing())
            {
                Debug.LogError("[Verify] 中文字体缺失: " + FontPath);
                problems++;
            }

            // 统计可点击物体数量：试题要求至少两个可观察或点击的场景对象
            int clickable = 0;
            foreach (MeshRenderer mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (mr.GetComponent<Collider>() != null) clickable++;
            }

            if (problems == 0)
            {
                Debug.Log(string.Format("[Verify] 通过：引用完整，可点击场景物体 {0} 个", clickable));
            }
            else
            {
                Debug.LogError(string.Format("[Verify] 失败：发现 {0} 个问题，详见上方日志", problems));
            }
        }

        static bool FontPathMissing()
        {
            return AssetDatabase.LoadAssetAtPath<Font>(FontPath) == null;
        }

        // ------------------------------------------------------------ 环境

        static void ConfigureLighting()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.36f, 0.38f, 0.42f);
            RenderSettings.fog = false;

            GameObject sunGo = new GameObject("Sun");
            sunGo.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            Light sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.97f, 0.92f);
            sun.intensity = 1.05f;
            sun.shadows = LightShadows.Soft;

            AddPointLight("Fill_Center", new Vector3(0f, 4.2f, 3f), 16f, 1.9f);
            AddPointLight("Fill_Left", new Vector3(-6f, 3.8f, 1f), 12f, 1.1f);
            AddPointLight("Fill_Right", new Vector3(6f, 3.8f, 1f), 12f, 1.1f);
        }

        static void AddPointLight(string name, Vector3 position, float range, float intensity)
        {
            GameObject go = new GameObject(name);
            go.transform.position = position;
            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = range;
            light.intensity = intensity;
            light.color = new Color(1f, 0.98f, 0.94f);
        }

        static void BuildEnvironment(Material floorMat, Material wallMat, Material cabinetMat,
            Material pipeMat, Material accentMat)
        {
            GameObject root = new GameObject("Environment");
            Transform t = root.transform;

            // 地面 30 x 30
            MakePrimitive("Ground", PrimitiveType.Plane, new Vector3(0f, 0f, 0f),
                new Vector3(3f, 1f, 3f), floorMat, t);

            // 三面墙（不封顶，方便俯视观察，也避免室内过暗）
            MakePrimitive("Wall_Back", PrimitiveType.Cube, new Vector3(0f, 2.5f, 8f),
                new Vector3(18f, 5f, 0.3f), wallMat, t);
            MakePrimitive("Wall_Left", PrimitiveType.Cube, new Vector3(-9f, 2.5f, 0f),
                new Vector3(0.3f, 5f, 16f), wallMat, t);
            MakePrimitive("Wall_Right", PrimitiveType.Cube, new Vector3(9f, 2.5f, 0f),
                new Vector3(0.3f, 5f, 16f), wallMat, t);

            // 四台机柜，正面朝向玩家初始位置
            for (int i = 0; i < 4; i++)
            {
                float x = -4.5f + i * 3f;
                GameObject cabinet = MakePrimitive("Cabinet_" + (i + 1), PrimitiveType.Cube,
                    new Vector3(x, 1f, 6f), new Vector3(1.2f, 2f, 1f), cabinetMat, t);

                // 机柜面板上的一条装饰条，方便在画面上分辨正面
                MakePrimitive("Cabinet_" + (i + 1) + "_Panel", PrimitiveType.Cube,
                    new Vector3(x, 1.35f, 5.48f), new Vector3(0.9f, 0.5f, 0.06f), accentMat, cabinet.transform);
            }

            // 顶部管线：沿 X 方向横穿机房
            GameObject pipeA = MakePrimitive("Pipe_Main", PrimitiveType.Cylinder, new Vector3(0f, 3.4f, 6.9f),
                new Vector3(0.1f, 5f, 0.1f), pipeMat, t);
            pipeA.transform.rotation = Quaternion.Euler(0f, 0f, 90f);

            GameObject pipeB = MakePrimitive("Pipe_Secondary", PrimitiveType.Cylinder, new Vector3(0f, 2.95f, 6.9f),
                new Vector3(0.07f, 5f, 0.07f), pipeMat, t);
            pipeB.transform.rotation = Quaternion.Euler(0f, 0f, 90f);

            // 配电箱（贴后墙）
            MakePrimitive("Power_Box", PrimitiveType.Cube, new Vector3(-7.4f, 2.1f, 7.6f),
                new Vector3(0.9f, 1.2f, 0.35f), accentMat, t);

            // 消防柜（贴右墙）
            MakePrimitive("Fire_Cabinet", PrimitiveType.Cube, new Vector3(8.5f, 0.7f, 1.5f),
                new Vector3(0.5f, 1.4f, 0.9f), accentMat, t);

            // 地沟盖板，用来验证「射线命中地面也能正确放置标记」
            MakePrimitive("Floor_Trench", PrimitiveType.Cube, new Vector3(0f, 0.02f, -1.5f),
                new Vector3(8f, 0.04f, 0.8f), accentMat, t);

            // 工作台
            MakePrimitive("Workbench", PrimitiveType.Cube, new Vector3(6.5f, 0.4f, 3.5f),
                new Vector3(2f, 0.8f, 1f), cabinetMat, t);
        }

        // ------------------------------------------------------------ 玩家

        static Camera BuildPlayer()
        {
            GameObject player = new GameObject("Player");
            player.transform.position = new Vector3(0f, 0.1f, -3f);
            // 放在 Ignore Raycast 层，标注射线就不会打到自己身上
            player.layer = 2;

            CharacterController controller = player.AddComponent<CharacterController>();
            controller.height = 1.75f;
            controller.radius = 0.32f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.slopeLimit = 45f;
            controller.stepOffset = 0.3f;

            GameObject pivot = new GameObject("CameraPivot");
            pivot.transform.SetParent(player.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 1.62f, 0f);

            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(pivot.transform, false);
            camGo.transform.localPosition = Vector3.zero;

            Camera cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 68f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 80f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.06f, 0.08f);

            // 场景里必须有且只有一个 AudioListener，否则 Unity 会持续告警
            camGo.AddComponent<AudioListener>();

            PlayerController pc = player.AddComponent<PlayerController>();
            pc.cameraPivot = pivot.transform;

            return cam;
        }

        // ------------------------------------------------------------ 管理器

        static GameObject BuildManagers(Camera cam)
        {
            GameObject go = new GameObject("GameManager");

            ApiClient api = go.AddComponent<ApiClient>();
            api.baseUrl = "http://127.0.0.1:8080";

            GameObject markerRoot = new GameObject("Markers");

            MarkerSpawner spawner = go.AddComponent<MarkerSpawner>();
            spawner.viewCamera = cam;
            spawner.markerRoot = markerRoot.transform;

            go.AddComponent<MarkerRestorer>();

            return go;
        }

        // ------------------------------------------------------------ UI

        static GameObject BuildUI(Font font, out Text counterText)
        {
            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                GameObject es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }

            GameObject canvasGo = new GameObject("Canvas");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            InspectionUI ui = canvasGo.AddComponent<InspectionUI>();

            counterText = BuildStatusBar(canvasGo.transform, font, ui);
            BuildFormPanel(canvasGo.transform, font, ui);
            BuildDetailPanel(canvasGo.transform, font, ui);
            BuildToast(canvasGo.transform, font, ui);

            return canvasGo;
        }

        /// <summary>顶部操作提示 + 右上角历史计数，返回计数文本供 MarkerRestorer 使用。</summary>
        static Text BuildStatusBar(Transform parent, Font font, InspectionUI ui)
        {
            RectTransform bg = FixedRect("StatusBar_bg", parent, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(16f, -12f), new Vector2(1360f, 40f));
            Image bgImage = bg.gameObject.AddComponent<Image>();
            bgImage.color = new Color(0.06f, 0.07f, 0.09f, 0.72f);
            bgImage.raycastTarget = false;

            Text status = MakeText("StatusBar", bg, "", 16, TextMain, font,
                16f, 10f, 1328f, 20f, TextAnchor.MiddleLeft);
            ui.statusBar = status;

            RectTransform counterRt = FixedRect("Counter_bg", parent, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-16f, -12f), new Vector2(300f, 40f));
            Image counterBg = counterRt.gameObject.AddComponent<Image>();
            counterBg.color = new Color(0.06f, 0.07f, 0.09f, 0.72f);
            counterBg.raycastTarget = false;

            return MakeText("Counter", counterRt, "正在加载历史问题…", 15, TextMuted, font,
                12f, 10f, 276f, 20f, TextAnchor.MiddleRight);
        }

        static void BuildFormPanel(Transform parent, Font font, InspectionUI ui)
        {
            // 高度按「错误提示最多占两行」预留：网络失败时的文案约 40 个汉字，
            // 只留一行会被截断或压到按钮上，这是肉眼可见的缺陷。
            RectTransform panel = FixedRect("FormPanel", parent, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-28f, 0f), new Vector2(440f, 468f));
            Image bg = panel.gameObject.AddComponent<Image>();
            bg.color = PanelBg;

            MakeText("Title", panel, "上报问题", 22, TextMain, font, 16f, 14f, 408f, 30f,
                TextAnchor.MiddleLeft, FontStyle.Bold);

            MakeText("Label_Title", panel, "标题（必填）", 14, TextMuted, font, 16f, 54f, 408f, 22f);
            ui.titleInput = MakeInputField("TitleInput", panel, "例如：3 号机柜温度异常", 16, font,
                16f, 78f, 408f, 40f, false);

            MakeText("Label_Desc", panel, "描述（可选）", 14, TextMuted, font, 16f, 130f, 408f, 22f);
            ui.descInput = MakeInputField("DescInput", panel, "补充现象、位置等细节", 15, font,
                16f, 154f, 408f, 86f, true);

            MakeText("Label_Priority", panel, "优先级", 14, TextMuted, font, 16f, 252f, 408f, 22f);

            ui.priorityButtons = new Button[3];
            ui.priorityLabels = new Text[3];
            for (int i = 0; i < 3; i++)
            {
                string label = ProblemPriority.ToChinese(ProblemPriority.All[i]);
                Text labelText;
                ui.priorityButtons[i] = MakeButton("Priority_" + ProblemPriority.All[i], panel, label, 16, font,
                    new Color(0.88f, 0.90f, 0.93f), 16f + i * 140f, 276f, 128f, 38f, out labelText);
                labelText.color = new Color(0.24f, 0.27f, 0.31f);
                ui.priorityLabels[i] = labelText;
            }

            ui.positionText = MakeText("PositionText", panel, "标记位置：未选择", 14, TextMuted, font,
                16f, 326f, 408f, 22f);

            ui.formErrorText = MakeText("FormError", panel, "", 13, new Color(0.95f, 0.42f, 0.38f), font,
                16f, 352f, 408f, 44f, TextAnchor.UpperLeft);

            Text submitLabel;
            ui.submitButton = MakeButton("SubmitButton", panel, "提交", 17, font, PrimaryButton,
                16f, 404f, 190f, 44f, out submitLabel, FontStyle.Bold);
            ui.submitLabel = submitLabel;

            ui.cancelButton = MakeButton("CancelButton", panel, "取消", 17, font, NeutralButton,
                232f, 404f, 190f, 44f, out _);

            ui.formPanel = panel.gameObject;
            panel.gameObject.SetActive(false);
        }

        static void BuildDetailPanel(Transform parent, Font font, InspectionUI ui)
        {
            RectTransform panel = FixedRect("DetailPanel", parent, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(24f, 24f), new Vector2(480f, 300f));
            Image bg = panel.gameObject.AddComponent<Image>();
            bg.color = PanelBg;

            MakeText("Title", panel, "问题详情", 20, TextMain, font, 16f, 14f, 448f, 28f,
                TextAnchor.MiddleLeft, FontStyle.Bold);

            ui.detailText = MakeText("DetailText", panel, "", 15, TextMain, font,
                16f, 48f, 448f, 200f, TextAnchor.UpperLeft);

            Text closeLabel;
            ui.detailCloseButton = MakeButton("DetailClose", panel, "关闭", 15, font, NeutralButton,
                16f, 258f, 120f, 32f, out closeLabel);

            ui.detailPanel = panel.gameObject;
            panel.gameObject.SetActive(false);
        }

        static void BuildToast(Transform parent, Font font, InspectionUI ui)
        {
            RectTransform panel = FixedRect("ToastPanel", parent, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 120f), new Vector2(760f, 52f));
            Image bg = panel.gameObject.AddComponent<Image>();
            bg.color = new Color(0.09f, 0.45f, 0.27f);
            bg.raycastTarget = false;

            ui.toastText = MakeText("ToastText", panel, "", 17, Color.white, font,
                16f, 0f, 728f, 52f, TextAnchor.MiddleLeft);

            ui.toastPanel = panel.gameObject;
            ui.toastBackground = bg;
            panel.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------ UI 基础件

        static RectTransform FixedRect(string name, Transform parent, Vector2 anchor, Vector2 pivot,
            Vector2 anchoredPos, Vector2 size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            return rt;
        }

        /// <summary>以父物体左上角为原点摆放的子元素。表单里所有控件都用这个坐标系，改布局最直观。</summary>
        static RectTransform TopLeftRect(string name, Transform parent, float x, float y, float w, float h)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        static Text MakeText(string name, Transform parent, string content, int fontSize, Color color,
            Font font, float x, float y, float w, float h,
            TextAnchor anchor = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            RectTransform rt = TopLeftRect(name, parent, x, y, w, h);
            Text text = rt.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.color = color;
            text.text = content;
            text.alignment = anchor;
            text.fontStyle = style;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            // 文字不该拦截鼠标，否则点在文字上会被当成点在 UI 上
            text.raycastTarget = false;
            return text;
        }

        static InputField MakeInputField(string name, Transform parent, string placeholder, int fontSize,
            Font font, float x, float y, float w, float h, bool multiline)
        {
            RectTransform rt = TopLeftRect(name, parent, x, y, w, h);

            Image bg = rt.gameObject.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.96f);

            InputField input = rt.gameObject.AddComponent<InputField>();

            RectTransform textRt = TopLeftRect("Text", rt, 10f, multiline ? 8f : 0f,
                w - 20f, multiline ? h - 16f : h);
            Text text = textRt.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.color = new Color(0.10f, 0.12f, 0.16f);
            text.supportRichText = false;
            text.alignment = multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
            text.horizontalOverflow = multiline ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;

            RectTransform phRt = TopLeftRect("Placeholder", rt, 10f, multiline ? 8f : 0f,
                w - 20f, multiline ? h - 16f : h);
            Text ph = phRt.gameObject.AddComponent<Text>();
            ph.font = font;
            ph.fontSize = fontSize;
            ph.color = new Color(0.56f, 0.59f, 0.63f);
            ph.text = placeholder;
            ph.fontStyle = FontStyle.Italic;
            ph.alignment = text.alignment;
            ph.horizontalOverflow = text.horizontalOverflow;
            ph.verticalOverflow = VerticalWrapMode.Truncate;
            ph.raycastTarget = false;

            input.textComponent = text;
            input.placeholder = ph;
            input.targetGraphic = bg;
            input.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
            input.caretColor = new Color(0.10f, 0.12f, 0.16f);
            input.selectionColor = new Color(0.30f, 0.60f, 0.90f, 0.45f);
            input.customCaretColor = true;
            input.characterLimit = 2000;

            return input;
        }

        static Button MakeButton(string name, Transform parent, string label, int fontSize, Font font,
            Color background, float x, float y, float w, float h, out Text labelText,
            FontStyle style = FontStyle.Normal)
        {
            RectTransform rt = TopLeftRect(name, parent, x, y, w, h);

            Image bg = rt.gameObject.AddComponent<Image>();
            bg.color = background;

            Button button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;

            RectTransform labelRt = TopLeftRect("Label", rt, 0f, 0f, w, h);
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;

            Text text = labelRt.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.text = label;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.fontStyle = style;
            text.raycastTarget = false;

            labelText = text;
            return button;
        }

        // ------------------------------------------------------------ 杂项

        static GameObject MakePrimitive(string name, PrimitiveType type, Vector3 position, Vector3 scale,
            Material material, Transform parent)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.localScale = scale;

            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null && material != null)
            {
                renderer.sharedMaterial = material;
            }
            return go;
        }

        static Material GetOrCreateMaterial(string name, Color color)
        {
            string path = MaterialDir + "/" + name + ".mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Shader shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("[Builder] 找不到可用 Shader，材质无法创建");
                return null;
            }

            Material mat = new Material(shader);
            mat.color = color;
            mat.name = name + "_Mat";
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static Font LoadFont()
        {
            Font font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font != null) return font;

            // 兜底：内置字体不含中文，只会显示方块，但至少不会因为空引用打断建场景
            try
            {
                return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
#endif
