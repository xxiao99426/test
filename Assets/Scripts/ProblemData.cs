using System;
using UnityEngine;

namespace InspectionDemo
{
    /// <summary>
    /// 优先级与状态的合法取值，与 Go 侧 server/model.go、React 侧 src/types.ts 一一对应。
    /// 三端字段名和枚举值不许出现偏差，改动时请三处一起改。
    /// </summary>
    public static class ProblemPriority
    {
        public const string Low = "low";
        public const string Medium = "medium";
        public const string High = "high";

        public static readonly string[] All = { Low, Medium, High };

        public static string ToChinese(string priority)
        {
            switch (priority)
            {
                case Low: return "低";
                case High: return "高";
                default: return "中";
            }
        }

        /// <summary>标记颜色由优先级决定：高危红、中危橙、低危蓝。</summary>
        public static Color ToColor(string priority)
        {
            switch (priority)
            {
                case Low: return new Color(0.34f, 0.56f, 0.86f);
                case High: return new Color(0.88f, 0.21f, 0.23f);
                default: return new Color(0.95f, 0.62f, 0.12f);
            }
        }
    }

    public static class ProblemStatus
    {
        public const string Open = "open";
        public const string InProgress = "in_progress";
        public const string Resolved = "resolved";

        public static readonly string[] All = { Open, InProgress, Resolved };

        public static string ToChinese(string status)
        {
            switch (status)
            {
                case InProgress: return "处理中";
                case Resolved: return "已解决";
                default: return "待处理";
            }
        }

        /// <summary>已解决的问题标记变灰，避免现场视图被历史问题干扰。</summary>
        public static Color ToColor(string status)
        {
            switch (status)
            {
                case InProgress: return new Color(0.95f, 0.62f, 0.12f);
                case Resolved: return new Color(0.55f, 0.60f, 0.64f);
                default: return new Color(0.88f, 0.21f, 0.23f);
            }
        }
    }

    [Serializable]
    public class PositionDto
    {
        public float x;
        public float y;
        public float z;

        public PositionDto() { }

        public PositionDto(Vector3 v)
        {
            x = v.x;
            y = v.y;
            z = v.z;
        }

        public Vector3 ToVector3()
        {
            return new Vector3(x, y, z);
        }

        public override string ToString()
        {
            return string.Format("({0:F2}, {1:F2}, {2:F2})", x, y, z);
        }
    }

    /// <summary>与试题第 4 节数据结构完全一致的字段集合。JsonUtility 按字段名序列化，不能改名。</summary>
    [Serializable]
    public class ProblemDto
    {
        public string id;
        public string title;
        public string description;
        public string priority;
        public string status;
        public PositionDto position;
        public string createdAt;
        public string updatedAt;
    }

    /// <summary>GET /api/problems 的响应。Unity 的 JsonUtility 不能反序列化顶层数组，所以后端包了一层 items。</summary>
    [Serializable]
    public class ProblemListDto
    {
        public ProblemDto[] items;
        public int total;
    }

    /// <summary>POST /api/problems 的请求体。id / status / 时间戳全部由后端生成，客户端不传。</summary>
    [Serializable]
    public class CreateProblemRequest
    {
        public string title;
        public string description;
        public string priority;
        public PositionDto position;
    }

    /// <summary>后端统一错误响应 { code, message, field }。</summary>
    [Serializable]
    public class ErrorDto
    {
        public string code;
        public string message;
        public string field;
    }
}
