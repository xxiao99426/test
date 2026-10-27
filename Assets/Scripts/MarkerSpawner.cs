using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace InspectionDemo
{
    /// <summary>
    /// 负责「点击 → 射线 → 放标记」这一条主线，同时守住三条硬性约束：
    ///   1. 点到 UI 时不能穿透到场景（试题 3.1 第 7 条）
    ///   2. 标记放在 hit.point 而不是碰撞体中心（试题 3.1 第 3、4 条）
    ///   3. 任何时刻最多只有一个待提交标记（试题 3.1 第 8 条）
    /// </summary>
    public class MarkerSpawner : MonoBehaviour
    {
        [Tooltip("用于发射射线的相机，通常是玩家身上的 Main Camera")]
        public Camera viewCamera;

        [Tooltip("射线最远距离（米）")]
        public float maxDistance = 40f;

        [Tooltip("待提交标记的球体直径")]
        public float pendingHeadDiameter = 0.22f;

        [Tooltip("已保存标记的球体直径")]
        public float savedHeadDiameter = 0.18f;

        [Tooltip("所有标记的父节点，留空则挂在本对象下")]
        public Transform markerRoot;

        [Tooltip("参与射线检测的层。默认排除第 2 层 Ignore Raycast，玩家角色就放在那一层")]
        public LayerMask blockingMask = ~(1 << 2);

        /// <summary>待提交标记被创建或移动时触发，UI 用它刷新坐标显示。</summary>
        public event Action<MarkerVisual> PendingChanged;

        /// <summary>点击了已保存的标记，UI 用它打开详情。</summary>
        public event Action<MarkerVisual> SavedMarkerClicked;

        MarkerVisual _pending;

        // UI 命中探测的复用对象，避免每次点击都产生 GC
        readonly List<RaycastResult> _uiProbeResults = new List<RaycastResult>();
        readonly List<Graphic> _uiGraphics = new List<Graphic>();
        PointerEventData _uiProbe;
        Canvas _uiCanvas;

        public MarkerVisual Pending { get { return _pending; } }
        public bool HasPending { get { return _pending != null; } }
        public Transform Root { get { return markerRoot != null ? markerRoot : transform; } }

        void Update()
        {
            if (!Input.GetMouseButtonDown(0)) return;
            HandleClick(Input.mousePosition);
        }

        /// <summary>
        /// 处理「屏幕上某一点被左键点击」。
        /// Update 传入真实鼠标位置；自检脚本可以直接传入 UI 控件的屏幕坐标，
        /// 于是「点 UI 不穿透」这条也能在没人操作的情况下验证，且走的是同一份代码。
        /// </summary>
        public void HandleClick(Vector2 screenPoint)
        {
            // ---- 约束 1：UI 上的点击不算场景点击 ----
            // 少了这一步，在输入框里点一下就会在地上多一个标记，这是最典型的失分点。
            if (IsPointerOverUI(screenPoint))
            {
                Debug.Log("[Spawner] 点击落在 UI 上，已拦截，不穿透到场景");
                return;
            }

            TryPlaceFromScreenPoint(screenPoint);
        }

        /// <summary>
        /// 给定屏幕坐标是否落在任意 UI 控件上。
        /// 用 RaycastAll 显式指定坐标，而不是依赖 IsPointerOverGameObject() 读取「当前指针位置」，
        /// 因为后者只能读到真实鼠标，脚本无法伪造，规则就没法自动验证。
        /// </summary>
        public bool IsPointerOverUI(Vector2 screenPoint)
        {
            if (EventSystem.current != null)
            {
                if (_uiProbe == null) _uiProbe = new PointerEventData(EventSystem.current);
                _uiProbe.position = screenPoint;
                _uiProbeResults.Clear();
                EventSystem.current.RaycastAll(_uiProbe, _uiProbeResults);
                if (_uiProbeResults.Count > 0) return true;
            }

            return GeometryBlocksAt(screenPoint);
        }

        /// <summary>
        /// 兜底几何判定：GraphicRaycaster 会跳过 Graphic.depth == -1 的控件，而 depth 是在画布
        /// 完成一次合批时才被赋值的。画布刚被 SetActive(true)、或者当前没有任何一帧被渲染时，
        /// 所有控件的 depth 都是 -1，上面那步必定返回空，点击就会穿透到场景里留下一个标记。
        /// 这里按 RectTransform 的屏幕矩形直接判断，不依赖合批状态。
        /// </summary>
        bool GeometryBlocksAt(Vector2 screenPoint)
        {
            if (_uiCanvas == null) _uiCanvas = FindFirstObjectByType<Canvas>();
            if (_uiCanvas == null) return false;

            IList<Graphic> registered = GraphicRegistry.GetRaycastableGraphicsForCanvas(_uiCanvas);
            if (registered == null) return false;

            for (int i = 0; i < registered.Count; i++)
            {
                Graphic graphic = registered[i];
                if (graphic == null || !graphic.raycastTarget) continue;
                if (!graphic.isActiveAndEnabled) continue;
                if (graphic.canvasRenderer.cull) continue;

                RectTransform rt = graphic.rectTransform;
                if (RectTransformUtility.RectangleContainsScreenPoint(rt, screenPoint, null))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 从屏幕坐标发射射线，并在命中点放置/移动待提交标记。
        /// 真实点击和自动化自检都走这一个入口，验证的不是复制品而是真代码。
        /// 返回 true 表示命中了场景物体（或点中了已有标记）。
        /// </summary>
        public bool TryPlaceFromScreenPoint(Vector2 screenPoint)
        {
            if (viewCamera == null)
            {
                Debug.LogWarning("[Spawner] 未设置 viewCamera，无法发射射线");
                return false;
            }

            Ray ray = viewCamera.ScreenPointToRay(screenPoint);
            RaycastHit hit;
            if (!Physics.Raycast(ray, out hit, maxDistance, blockingMask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            // 点到了已有标记：已保存的打开详情，待提交的不做处理（避免自己点自己）
            MarkerVisual clicked = hit.collider.GetComponentInParent<MarkerVisual>();
            if (clicked != null)
            {
                if (!clicked.isPending && SavedMarkerClicked != null)
                {
                    SavedMarkerClicked(clicked);
                }
                return true;
            }

            // ---- 约束 2、3：用 hit.point 放置，且复用同一个待提交标记 ----
            PlaceOrMovePending(hit.point, hit.normal);
            return true;
        }

        /// <summary>
        /// 放置或移动待提交标记。
        /// 移动而不是「销毁再新建」，是为了让对象数量始终为 1——这样「一次有效点击最多创建一个
        /// 待提交标记」在运行时的对象层级里是可验证的，而不是靠开发者口头保证。
        /// </summary>
        void PlaceOrMovePending(Vector3 hitPoint, Vector3 surfaceNormal)
        {
            Quaternion rotation = FacingRotation(surfaceNormal);

            if (_pending == null)
            {
                _pending = CreateMarker("Marker_Pending", hitPoint, rotation, pendingHeadDiameter, null, true);
                Debug.Log(string.Format("[Spawner] 放置待提交标记 hit.point=({0:F2}, {1:F2}, {2:F2}) normal=({3:F2}, {4:F2}, {5:F2})",
                    hitPoint.x, hitPoint.y, hitPoint.z, surfaceNormal.x, surfaceNormal.y, surfaceNormal.z));
            }
            else
            {
                _pending.transform.position = hitPoint;
                _pending.transform.rotation = rotation;
                // 移动后同样要同步，否则下一次点击可能命中标记的旧位置
                Physics.SyncTransforms();
                Debug.Log(string.Format("[Spawner] 移动待提交标记到 ({0:F2}, {1:F2}, {2:F2})",
                    hitPoint.x, hitPoint.y, hitPoint.z));
            }

            if (PendingChanged != null) PendingChanged(_pending);
        }

        /// <summary>
        /// 让标记的本地 +Z 对准表面法线：杆沿法线伸出，球始终浮在表面上。
        /// 地面法线与世界 up 平行，此时 Quaternion.LookRotation 需要一个不平行的 up 参考，
        /// 否则 Unity 会打警告并返回单位四元数。
        /// </summary>
        static Quaternion FacingRotation(Vector3 normal)
        {
            if (normal.sqrMagnitude < 0.0001f) normal = Vector3.up;
            normal.Normalize();

            Vector3 upRef = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.99f
                ? Vector3.forward
                : Vector3.up;

            return Quaternion.LookRotation(normal, upRef);
        }

        /// <summary>提交成功后把待提交标记转为已保存状态，返回它。</summary>
        public MarkerVisual CommitPending(ProblemDto dto)
        {
            if (_pending == null) return null;

            MarkerVisual committed = _pending;
            committed.name = "Marker_" + (dto != null && !string.IsNullOrEmpty(dto.id) ? dto.id : "saved");
            SetHeadDiameter(committed, savedHeadDiameter);
            committed.Configure(dto, false);

            _pending = null;
            return committed;
        }

        /// <summary>取消填写时销毁未保存标记（试题 3.1 第 8 条）。</summary>
        public void DiscardPending()
        {
            if (_pending == null) return;

            GameObject go = _pending.gameObject;

            // 先禁用再销毁：Destroy 要到帧末才真正回收，中间这一帧里物体仍然可见、
            // Collider 仍然可以被射线打中，会出现「已经取消了但下一秒还能点到一个幽灵标记」。
            // SetActive(false) 让它在当前帧立刻退出渲染与物理查询，Destroy 负责释放。
            go.SetActive(false);
            Destroy(go);

            _pending = null;
            Debug.Log("[Spawner] 移除未保存标记");
        }

        /// <summary>从后端恢复一条已保存的问题标记。</summary>
        public MarkerVisual CreateSavedMarker(ProblemDto dto)
        {
            if (dto == null) return null;

            // 后端只存了坐标，没有存表面法线（第 4 节的数据结构里就没有这个字段）。
            // 所以恢复时不能沿用「朝向法线」，否则贴墙标记的球头会沿 +Z 伸进柜体内部看不见。
            // 统一让标记竖直立起：杆沿世界 Y，贴在垂直墙面时正好落在墙面内，球露在墙外。
            // 代价是杆的朝向与首次放置时略有差异，但标记坐标本身完全一致。
            Quaternion rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);

            Vector3 position = dto.position != null ? dto.position.ToVector3() : Vector3.zero;
            string name = "Marker_" + (string.IsNullOrEmpty(dto.id) ? "saved" : dto.id);

            return CreateMarker(name, position, rotation, savedHeadDiameter, dto, false);
        }

        MarkerVisual CreateMarker(string name, Vector3 position, Quaternion rotation,
            float headDiameter, ProblemDto dto, bool pending)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(Root, true);
            root.transform.position = position;
            root.transform.rotation = rotation;

            // 短杆：从命中点沿法线伸出，提示标记确实钉在这个位置
            GameObject stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stem.name = "Stem";
            stem.transform.SetParent(root.transform, false);
            stem.transform.localPosition = new Vector3(0f, 0f, 0.11f);
            stem.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            stem.transform.localScale = new Vector3(0.012f, 0.11f, 0.012f);
            // 杆不需要参与射线检测，去掉碰撞体可减少射线命中噪声
            Destroy(stem.GetComponent<Collider>());

            // 球头：唯一可被点中的部件
            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0f, 0f, 0.26f);
            head.transform.localScale = Vector3.one * headDiameter;

            MarkerVisual visual = root.AddComponent<MarkerVisual>();
            visual.headRenderer = head.GetComponent<MeshRenderer>();
            visual.Configure(dto, pending);

            // Unity 2018.3 之后默认关闭 Auto Sync Transforms：代码改完 Transform，
            // 物理引擎的 broadphase 不会立刻知道。不手动同步的话，刚建出来的标记
            // 在接下来几帧里打不中（collider 还停在旧位置/旧尺寸），
            // 表现就是「点标记没反应」，只能靠瞎点碰运气。
            Physics.SyncTransforms();

            return visual;
        }

        static void SetHeadDiameter(MarkerVisual visual, float diameter)
        {
            if (visual == null || visual.headRenderer == null) return;

            Transform head = visual.headRenderer.transform;
            head.localScale = Vector3.one * diameter;

            // 同上：缩放改了，collider 的包围盒也要立刻同步
            Physics.SyncTransforms();
        }
    }
}
