using UnityEngine;
using UnityEngine.UI;

namespace InspectionDemo
{
    /// <summary>
    /// 进入场景时从 Go 后端拉取已保存的问题，并在对应坐标恢复标记。
    /// 这是加分项「从后端加载问题，并在固定虚拟场景中恢复对应标记」的落点，
    /// 同时也是「三端使用同一份数据」最直观的证据：在 React 里改的状态，
    /// 重新进入 Unity 后标记颜色会跟着变。
    /// </summary>
    public class MarkerRestorer : MonoBehaviour
    {
        public ApiClient api;
        public MarkerSpawner spawner;
        public InspectionUI ui;

        [Tooltip("恢复完成后显示在右上角的计数文本，可为空")]
        public Text counterText;

        // 场景里当前的问题标记总数（启动时恢复的 + 本次会话新提交的）
        int _count;

        void Start()
        {
            if (api == null || spawner == null)
            {
                Debug.LogWarning("[Restorer] 缺少 api 或 spawner 引用，跳过历史标记恢复");
                return;
            }

            api.FetchProblems(OnLoaded, OnFailed);
        }

        void OnLoaded(ProblemDto[] items)
        {
            int restored = 0;

            if (items != null)
            {
                foreach (ProblemDto dto in items)
                {
                    if (spawner.CreateSavedMarker(dto) != null) restored++;
                }
            }

            Debug.Log(string.Format("[Restorer] 已从后端恢复 {0} 个问题标记", restored));

            _count = restored;
            UpdateCounter();

            if (ui != null && restored > 0)
            {
                ui.ShowToast(string.Format("已从后端加载 {0} 条历史问题标记", restored), false);
            }
        }

        /// <summary>
        /// 本次会话内新上报成功一条时调用，让计数与场景保持一致。
        /// 不调用的话右上角会一直停在启动时的数字，看起来像"提交没生效"。
        /// </summary>
        public void NotifyProblemAdded()
        {
            _count++;
            UpdateCounter();
        }

        void UpdateCounter()
        {
            if (counterText != null)
            {
                counterText.text = string.Format("场景内问题 {0} 条", _count);
            }
        }

        void OnFailed(string message)
        {
            Debug.LogWarning("[Restorer] 加载历史问题失败: " + message);

            if (counterText != null)
            {
                counterText.text = "历史问题加载失败";
            }

            // 后端没起来也不该阻挡用户操作：场景照常可走，上报时会再提示一次具体原因
            if (ui != null)
            {
                ui.ShowToast("历史问题加载失败：" + message, true);
            }
        }
    }
}
