using UnityEngine;

namespace InspectionDemo
{
    /// <summary>
    /// 场景中的一个问题标记。结构刻意做成「钉在命中点的短杆 + 顶端的球」：
    /// 杆的底部就是射线命中位置，肉眼可以直接验证「标记是不是放在实际命中点」，
    /// 而不是放在物体中心。
    ///
    /// 层级：
    ///   Marker_xxx        (本组件，位置 = 命中点，朝向 = 表面法线)
    ///   ├─ Stem           (细圆柱，沿法线方向伸出)
    ///   └─ Head           (球，带 SphereCollider，负责被点中)
    /// </summary>
    public class MarkerVisual : MonoBehaviour
    {
        [Tooltip("已保存问题携带的数据；待提交标记为 null")]
        public ProblemDto data;

        [Tooltip("true = 已放置但还没提交，会有脉动效果")]
        public bool isPending;

        [Tooltip("球体渲染器，颜色按优先级/状态变化")]
        public MeshRenderer headRenderer;

        MaterialPropertyBlock _block;
        Vector3 _headBaseScale;
        float _phase;

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>待提交标记用青绿色，与已保存标记的优先级配色明显区分。</summary>
        static readonly Color PendingColor = new Color(0.19f, 0.81f, 0.60f);

        void Awake()
        {
            _block = new MaterialPropertyBlock();
            _phase = Random.Range(0f, Mathf.PI * 2f);
        }

        public void Configure(ProblemDto dto, bool pending)
        {
            data = dto;
            isPending = pending;

            if (_headBaseScale == Vector3.zero && headRenderer != null)
            {
                _headBaseScale = headRenderer.transform.localScale;
            }

            ApplyAppearance();
        }

        void ApplyAppearance()
        {
            if (headRenderer == null) return;

            Color c = ResolveColor();

            // 用 MaterialPropertyBlock 而不是 renderer.material：
            // 后者每次访问都会克隆一份材质，标记多了会持续泄漏，且合批被破坏。
            headRenderer.GetPropertyBlock(_block);
            _block.SetColor(ColorId, c);
            _block.SetColor(BaseColorId, c);
            headRenderer.SetPropertyBlock(_block);
        }

        Color ResolveColor()
        {
            if (isPending) return PendingColor;
            if (data == null) return Color.white;
            if (data.status == ProblemStatus.Resolved) return ProblemStatus.ToColor(data.status);
            return ProblemPriority.ToColor(data.priority);
        }

        void Update()
        {
            // 脉动只在待提交时出现。「已保存」和「未保存」在场景里一眼可分。
            if (!isPending || headRenderer == null || _headBaseScale == Vector3.zero) return;

            float t = 1f + 0.14f * Mathf.Sin(Time.time * 5.5f + _phase);
            headRenderer.transform.localScale = _headBaseScale * t;
        }

        /// <summary>详情面板要显示的文本。点标记能看到这条问题是什么。</summary>
        public string DescribeForUI()
        {
            if (data == null)
            {
                return string.Format(
                    "待提交标记\n位置 {0}\n\n填写表单后提交，成功后此处会变成已保存标记。",
                    PositionText());
            }

            string desc = string.IsNullOrEmpty(data.description) ? "（无描述）" : data.description;

            return string.Format(
                "标题：{0}\n优先级：{1}    状态：{2}\n位置：{3}\n上报时间：{4}\n\n描述：{5}\n\nID：{6}",
                data.title,
                ProblemPriority.ToChinese(data.priority),
                ProblemStatus.ToChinese(data.status),
                PositionText(),
                FormatTime(data.createdAt),
                desc,
                data.id);
        }

        string PositionText()
        {
            Vector3 p = transform.position;
            return string.Format("({0:F2}, {1:F2}, {2:F2})", p.x, p.y, p.z);
        }

        /// <summary>后端返回的是 RFC3339 UTC 字符串，Unity 不解析时间，只做本地化展示。</summary>
        static string FormatTime(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return "未知";

            System.DateTime parsed;
            if (System.DateTime.TryParse(iso, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out parsed))
            {
                return parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            }
            return iso;
        }
    }
}
