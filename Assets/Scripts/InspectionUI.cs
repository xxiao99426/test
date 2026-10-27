using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace InspectionDemo
{
    /// <summary>
    /// 场景内的全部 UI：顶部操作提示、上报表单、问题详情、结果提示条。
    /// 引用全部由 SceneBuilder 在生成场景时绑定，运行时不再做 Find。
    /// </summary>
    public class InspectionUI : MonoBehaviour
    {
        [Header("依赖")]
        public ApiClient api;
        public MarkerSpawner spawner;
        public MarkerRestorer restorer;

        [Header("顶部提示")]
        public Text statusBar;

        [Header("上报表单")]
        public GameObject formPanel;
        public InputField titleInput;
        public InputField descInput;
        public Text positionText;
        public Text formErrorText;
        public Button submitButton;
        public Text submitLabel;
        public Button cancelButton;

        [Tooltip("低 / 中 / 高，顺序必须与 ProblemPriority.All 一致")]
        public Button[] priorityButtons;
        public Text[] priorityLabels;

        [Header("详情面板")]
        public GameObject detailPanel;
        public Text detailText;
        public Button detailCloseButton;

        [Header("结果提示条")]
        public GameObject toastPanel;
        public Image toastBackground;
        public Text toastText;

        static readonly Color ToastOk = new Color(0.09f, 0.45f, 0.27f);
        static readonly Color ToastError = new Color(0.72f, 0.16f, 0.16f);

        string _selectedPriority = ProblemPriority.Medium;
        Coroutine _toastRoutine;
        bool _submitting;

        void Awake()
        {
            if (formPanel != null) formPanel.SetActive(false);
            if (detailPanel != null) detailPanel.SetActive(false);
            if (toastPanel != null) toastPanel.SetActive(false);

            if (spawner != null)
            {
                spawner.PendingChanged += OnPendingChanged;
                spawner.SavedMarkerClicked += OnSavedMarkerClicked;
            }

            if (submitButton != null) submitButton.onClick.AddListener(OnSubmitClicked);
            if (cancelButton != null) cancelButton.onClick.AddListener(OnCancelClicked);
            if (detailCloseButton != null) detailCloseButton.onClick.AddListener(CloseDetail);

            if (priorityButtons != null)
            {
                for (int i = 0; i < priorityButtons.Length; i++)
                {
                    int index = i; // 闭包捕获：必须复制到局部变量
                    if (priorityButtons[i] != null)
                    {
                        priorityButtons[i].onClick.AddListener(() => SelectPriority(index));
                    }
                }
            }

            UpdatePriorityButtons();
            UpdateStatusBar();
            SetFormError("");
        }

        void OnDestroy()
        {
            if (spawner != null)
            {
                spawner.PendingChanged -= OnPendingChanged;
                spawner.SavedMarkerClicked -= OnSavedMarkerClicked;
            }
        }

        void UpdateStatusBar()
        {
            if (statusBar == null) return;

            string backend = api != null ? api.baseUrl : "未配置后端";
            statusBar.text = string.Format(
                "左键点击设备或地面放置标记　·　WASD 移动　·　按住右键转视角　·　点击已有标记查看详情　·　后端 {0}",
                backend);
        }

        // ------------------------------------------------------------ 待提交标记

        void OnPendingChanged(MarkerVisual marker)
        {
            CloseDetail();

            bool wasOpen = formPanel != null && formPanel.activeSelf;
            if (formPanel != null) formPanel.SetActive(true);

            // 第一次放置时清空表单；在同一场巡检里挪动标记则保留已填内容，
            // 避免用户输入到一半点错位置就丢失。
            if (!wasOpen)
            {
                if (titleInput != null) titleInput.text = "";
                if (descInput != null) descInput.text = "";
                SelectPriority(1);
                SetFormError("");
            }

            if (positionText != null && marker != null)
            {
                Vector3 p = marker.transform.position;
                // 这里显示的就是点提交时会上报的坐标，两者同源
                positionText.text = string.Format("标记位置：({0:F2}, {1:F2}, {2:F2})", p.x, p.y, p.z);
            }

            if (titleInput != null) titleInput.ActivateInputField();
        }

        // ------------------------------------------------------------ 提交 / 取消

        void OnSubmitClicked()
        {
            if (_submitting) return;

            if (spawner == null || spawner.Pending == null)
            {
                ShowToast("当前没有待提交的标记，请先在场景中点击设备", true);
                return;
            }
            if (api == null)
            {
                ShowToast("未配置 ApiClient，无法上报", true);
                return;
            }

            string title = titleInput != null ? titleInput.text.Trim() : "";

            // 客户端先挡一次空标题。后端同样会挡（400），这里是减少一次无谓往返，
            // 后端的校验才是最终防线。
            if (string.IsNullOrEmpty(title))
            {
                SetFormError("标题不能为空");
                if (titleInput != null) titleInput.ActivateInputField();
                return;
            }
            if (title.Length > 200)
            {
                SetFormError("标题不能超过 200 字");
                return;
            }

            SetFormError("");
            _submitting = true;
            SetButtonsInteractable(false);
            if (submitLabel != null) submitLabel.text = "提交中…";

            Vector3 pos = spawner.Pending.transform.position;
            CreateProblemRequest payload = new CreateProblemRequest
            {
                title = title,
                description = descInput != null ? descInput.text.Trim() : "",
                priority = _selectedPriority,
                position = new PositionDto(pos),
            };

            api.CreateProblem(payload, OnCreateSuccess, OnCreateFailed);
        }

        void OnCreateSuccess(ProblemDto dto)
        {
            _submitting = false;
            SetButtonsInteractable(true);
            if (submitLabel != null) submitLabel.text = "提交";

            if (spawner != null) spawner.CommitPending(dto);
            if (formPanel != null) formPanel.SetActive(false);
            if (restorer != null) restorer.NotifyProblemAdded();

            ShowToast(string.Format("上报成功：「{0}」已保存到后端", dto.title), false);
        }

        void OnCreateFailed(string message)
        {
            _submitting = false;
            SetButtonsInteractable(true);
            if (submitLabel != null) submitLabel.text = "提交";

            // 失败时保留标记与已填内容，用户可以直接点「提交」重试，
            // 不需要重新找一个位置点第二次。
            SetFormError(message);
            ShowToast("上报失败：" + message, true);
        }

        void OnCancelClicked()
        {
            if (spawner != null) spawner.DiscardPending();
            if (formPanel != null) formPanel.SetActive(false);
            SetFormError("");
            ShowToast("已取消，未保存的标记已从场景中移除", false);
        }

        // ------------------------------------------------------------ 详情

        void OnSavedMarkerClicked(MarkerVisual marker)
        {
            if (detailPanel == null || marker == null) return;

            detailPanel.SetActive(true);
            if (detailText != null) detailText.text = marker.DescribeForUI();
        }

        public void CloseDetail()
        {
            if (detailPanel != null) detailPanel.SetActive(false);
        }

        // ------------------------------------------------------------ 优先级

        void SelectPriority(int index)
        {
            if (index < 0 || index >= ProblemPriority.All.Length) return;

            _selectedPriority = ProblemPriority.All[index];
            UpdatePriorityButtons();
        }

        void UpdatePriorityButtons()
        {
            if (priorityButtons == null) return;

            Color idle = new Color(0.88f, 0.90f, 0.93f);
            Color idleText = new Color(0.24f, 0.27f, 0.31f);

            for (int i = 0; i < priorityButtons.Length; i++)
            {
                Button btn = priorityButtons[i];
                if (btn == null) continue;

                string priority = ProblemPriority.All[i];
                bool selected = priority == _selectedPriority;

                Image img = btn.targetGraphic as Image;
                if (img != null)
                {
                    img.color = selected ? ProblemPriority.ToColor(priority) : idle;
                }

                if (priorityLabels != null && i < priorityLabels.Length && priorityLabels[i] != null)
                {
                    priorityLabels[i].color = selected ? Color.white : idleText;
                }
            }
        }

        // ------------------------------------------------------------ 提示条

        public void ShowToast(string message, bool isError)
        {
            if (toastPanel == null) return;

            if (_toastRoutine != null) StopCoroutine(_toastRoutine);

            toastPanel.SetActive(true);

            if (toastText != null)
            {
                toastText.text = message;
                toastText.color = Color.white;
            }
            if (toastBackground != null)
            {
                toastBackground.color = isError ? ToastError : ToastOk;
            }

            _toastRoutine = StartCoroutine(HideToastAfter(isError ? 5f : 2.6f));
        }

        IEnumerator HideToastAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (toastPanel != null) toastPanel.SetActive(false);
            _toastRoutine = null;
        }

        // ------------------------------------------------------------ 杂项

        void SetFormError(string message)
        {
            if (formErrorText == null) return;
            formErrorText.text = message;
            formErrorText.color = new Color(0.78f, 0.16f, 0.16f);
        }

        void SetButtonsInteractable(bool value)
        {
            if (submitButton != null) submitButton.interactable = value;
            // 提交在途时禁用取消，避免出现「请求还在飞、标记已被销毁」的不一致状态
            if (cancelButton != null) cancelButton.interactable = value;
        }
    }
}
