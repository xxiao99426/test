#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace InspectionDemo.EditorTools
{
    /// <summary>
    /// 运行时自检：在 Play 模式下验证「射线 → 命中点 → 单标记 → 取消回收」这条主线。
    ///
    /// 断言方式刻意做成「自检自己发一条探针射线，算出理论落点，再和被放置的标记比较」，
    /// 而不是把目标坐标写死。写死坐标会把布局改动变成假失败（例如机柜面板比柜体凸出 5cm，
    /// 命中点就会停在前者上，而实现完全正确）。
    ///
    /// 无法自动验证的部分：真实键盘鼠标输入（WASD 移动、右键转视角）需要人工操作，
    /// 其余包含「点 UI 不穿透」在内的规则都走自动化断言。
    /// </summary>
    public static class RuntimeSelfTest
    {
        static int _pass;
        static int _fail;

        [MenuItem("巡检工具/运行运行时自检（需先进入 Play）", false, 20)]
        public static void Run()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[SelfTest] 请先点击 Play 进入运行模式，再执行本自检。");
                return;
            }

            MarkerSpawner spawner = Object.FindFirstObjectByType<MarkerSpawner>();
            Camera cam = Object.FindFirstObjectByType<Camera>();
            InspectionUI ui = Object.FindFirstObjectByType<InspectionUI>();

            if (spawner == null || cam == null || ui == null)
            {
                Debug.LogError("[SelfTest] 场景缺少 MarkerSpawner / Camera / InspectionUI，无法自检。");
                return;
            }

            _pass = 0;
            _fail = 0;

            // 起点清理：上一次自检可能留下待提交标记
            spawner.DiscardPending();
            int savedBefore = CountMarkers(false);

            // ---------------------------------------------------------- T1 落点精度
            Vector2 screenA = cam.WorldToScreenPoint(new Vector3(-1.5f, 1.0f, 5.5f));

            // 探针：自检独立发一条同样的射线，得到「理论上应该放在哪」
            Ray probeRay = cam.ScreenPointToRay(screenA);
            RaycastHit probeHit;
            bool probeOk = Physics.Raycast(probeRay, out probeHit, spawner.maxDistance,
                spawner.blockingMask, QueryTriggerInteraction.Ignore);

            Record(probeOk, "T1 探针射线命中场景");
            Record(spawner.TryPlaceFromScreenPoint(screenA), "T1 放置入口返回成功");
            Record(spawner.Pending != null, "T1 生成待提交标记");

            if (probeOk && spawner.Pending != null)
            {
                Vector3 actual = spawner.Pending.transform.position;
                Vector3 expected = probeHit.point;

                float error = Vector3.Distance(actual, expected);
                Record(error < 0.001f, string.Format(
                    "T1 标记位置 == 射线命中点（误差 {0:F5} m）", error));

                // 反证：如果实现用了 collider.bounds.center，误差会是几十厘米量级
                float centerError = Vector3.Distance(actual, probeHit.collider.bounds.center);
                Record(centerError > 0.2f, string.Format(
                    "T1 标记没有落在物体中心（距命中物体中心 {0:F3} m）", centerError));

                Debug.Log(string.Format("[SelfTest] T1 命中物体={0} 命中点=({1:F3},{2:F3},{3:F3}) 法线=({4:F2},{5:F2},{6:F2})",
                    probeHit.collider.gameObject.name,
                    expected.x, expected.y, expected.z,
                    probeHit.normal.x, probeHit.normal.y, probeHit.normal.z));
            }
            else
            {
                Record(false, "T1 落点精度（缺少前置条件）");
            }

            // ---------------------------------------------------------- T2 单标记状态机
            Vector2 screenB = cam.WorldToScreenPoint(new Vector3(4.5f, 1.2f, 5.5f));
            Ray probeRayB = cam.ScreenPointToRay(screenB);
            RaycastHit probeHitB;
            bool probeOkB = Physics.Raycast(probeRayB, out probeHitB, spawner.maxDistance,
                spawner.blockingMask, QueryTriggerInteraction.Ignore);

            spawner.TryPlaceFromScreenPoint(screenB);

            int pendingCount = CountMarkers(true);
            Record(pendingCount == 1, string.Format(
                "T2 连续两次点击后待提交标记数量 = {0}（应为 1）", pendingCount));

            if (probeOkB && spawner.Pending != null)
            {
                float moved = Vector3.Distance(spawner.Pending.transform.position, probeHitB.point);
                Record(moved < 0.001f, string.Format(
                    "T2 标记移动到新命中点而不是新建第二个（误差 {0:F5} m）", moved));
            }

            // ---------------------------------------------------------- T3 取消回收
            spawner.DiscardPending();
            Record(spawner.Pending == null, "T3 取消后 Pending 引用已清空");
            Record(CountMarkers(true) == 0, "T3 场景中不再存在待提交标记");
            Record(CountMarkers(false) == savedBefore, "T3 已保存的历史标记没有被误删");

            // ---------------------------------------------------------- T4 点击 UI 不穿透
            // 这条原本只能人工验证。把「点击处理」抽成 HandleClick(screenPoint) 之后，
            // 自检可以直接喂 UI 控件的屏幕坐标进去，验证的是 Update 里同一份代码。
            if (ui.formPanel != null) ui.formPanel.SetActive(true);

            // 面板刚被激活时，Canvas 的批次还没重建，Graphic.depth 仍是 -1，
            // GraphicRaycaster 会按"未参与渲染"把这些控件跳过，射线检测结果永远为空。
            // 真机上用户点击发生在后续帧，Canvas 早已重建，不存在这个问题；
            // 自检把所有断言塞在同一帧里跑，所以必须手动强制重建一次。
            Canvas.ForceUpdateCanvases();

            int pendingBeforeUI = CountMarkers(true);

            Vector2 titlePoint = ScreenCenterOf(ui.titleInput.GetComponent<RectTransform>());
            Record(spawner.IsPointerOverUI(titlePoint), "T4 输入框位置被判定为 UI");
            spawner.HandleClick(titlePoint);
            Record(CountMarkers(true) == pendingBeforeUI, "T4 点击输入框没有在场景中产生标记");

            Vector2 submitPoint = ScreenCenterOf(ui.submitButton.GetComponent<RectTransform>());
            Record(spawner.IsPointerOverUI(submitPoint), "T4 提交按钮位置被判定为 UI");
            spawner.HandleClick(submitPoint);
            Record(CountMarkers(true) == pendingBeforeUI, "T4 点击提交按钮没有在场景中产生标记");

            // 反证：如果拦截条件写成「一律返回 true」，上面两条会白给，所以必须验反向
            Vector2 worldPoint = cam.WorldToScreenPoint(new Vector3(-1.5f, 1.0f, 5.5f));
            Record(!spawner.IsPointerOverUI(worldPoint), "T4 场景坐标没有被误判为 UI（反证）");
            spawner.HandleClick(worldPoint);
            Record(CountMarkers(true) == 1, "T4 同一入口在场景坐标上正常产生 1 个标记（反证）");

            spawner.DiscardPending();
            if (ui.formPanel != null) ui.formPanel.SetActive(false);

            // ---------------------------------------------------------- T5 引用完整性
            Record(ui.formPanel != null && ui.titleInput != null && ui.descInput != null,
                "T5 表单控件引用完整");
            Record(ui.submitButton != null && ui.cancelButton != null, "T5 提交/取消按钮引用完整");
            Record(ui.priorityButtons != null && ui.priorityButtons.Length == 3, "T5 优先级按钮为 3 个");
            Record(ui.toastPanel != null && ui.detailPanel != null, "T5 提示条与详情面板引用完整");
            Record(ui.api != null && ui.spawner != null, "T5 UI 已绑定 ApiClient 与 MarkerSpawner");
            Record(spawner.viewCamera != null, "T5 MarkerSpawner 已绑定相机");
            Record(ui.statusBar != null && ui.formErrorText != null, "T5 状态栏与错误提示引用完整");

            Debug.Log(string.Format("[SelfTest] ===== 结果：{0} 通过 / {1} 失败 =====", _pass, _fail));

            if (_fail == 0)
            {
                Debug.Log("[SelfTest] 全部通过。仍建议人工过一遍：WASD 移动、右键转视角、点标记看详情。");
            }
            else
            {
                Debug.LogError(string.Format("[SelfTest] 有 {0} 项失败，见上方逐条结果。", _fail));
            }
        }

        /// <summary>取一个 UI 控件在屏幕上的中心坐标。Overlay 画布传给 RectTransformUtility 的相机为 null。</summary>
        static Vector2 ScreenCenterOf(RectTransform rt)
        {
            return RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
        }

        static int CountMarkers(bool pending)        {
            int n = 0;
            foreach (MarkerVisual mv in Object.FindObjectsByType<MarkerVisual>(FindObjectsSortMode.None))
            {
                if (mv.isPending == pending) n++;
            }
            return n;
        }

        static void Record(bool ok, string name)
        {
            if (ok) _pass++;
            else _fail++;

            // 每条单独打一行日志：Console 里多行字符串会被折叠成首行，逐条输出才能逐条核对
            if (ok) Debug.Log("[SelfTest] PASS  " + name);
            else Debug.LogError("[SelfTest] FAIL  " + name);
        }
    }
}
#endif
