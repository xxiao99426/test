using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace InspectionDemo
{
    /// <summary>
    /// Unity 与 Go 后端之间的唯一通道。
    /// 所有网络错误都会被翻译成一句能指导用户操作的中文，而不是抛一个 "Unknown Error"。
    /// </summary>
    public class ApiClient : MonoBehaviour
    {
        [Tooltip("Go 后端地址。Editor 与后端同机时保持 127.0.0.1 即可")]
        public string baseUrl = "http://127.0.0.1:8080";

        [Tooltip("单次请求超时（秒）")]
        public int timeoutSeconds = 10;

        /// <summary>正在进行的请求数。UI 用它禁掉提交按钮，避免重复上报。</summary>
        public int InFlight { get; private set; }

        public bool IsBusy
        {
            get { return InFlight > 0; }
        }

        public void CreateProblem(CreateProblemRequest payload, Action<ProblemDto> onSuccess, Action<string> onError)
        {
            StartCoroutine(CreateRoutine(payload, onSuccess, onError));
        }

        public void FetchProblems(Action<ProblemDto[]> onSuccess, Action<string> onError)
        {
            StartCoroutine(FetchRoutine(onSuccess, onError));
        }

        string Url(string path)
        {
            return baseUrl.TrimEnd('/') + path;
        }

        IEnumerator CreateRoutine(CreateProblemRequest payload, Action<ProblemDto> onSuccess, Action<string> onError)
        {
            InFlight++;
            string json = JsonUtility.ToJson(payload);

            using (UnityWebRequest req = new UnityWebRequest(Url("/api/problems"), UnityWebRequest.kHttpVerbPOST))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = timeoutSeconds;

                yield return req.SendWebRequest();

                InFlight--;

                if (req.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(DescribeFailure(req));
                    yield break;
                }

                ProblemDto dto = ParseProblem(req.downloadHandler.text);
                if (dto == null || string.IsNullOrEmpty(dto.id))
                {
                    onError?.Invoke("后端返回的数据无法解析，请检查接口格式是否变更");
                    yield break;
                }

                Debug.Log($"[API] 上报成功 id={dto.id} title={dto.title}");
                onSuccess?.Invoke(dto);
            }
        }

        IEnumerator FetchRoutine(Action<ProblemDto[]> onSuccess, Action<string> onError)
        {
            InFlight++;

            using (UnityWebRequest req = UnityWebRequest.Get(Url("/api/problems")))
            {
                req.timeout = timeoutSeconds;
                yield return req.SendWebRequest();

                InFlight--;

                if (req.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(DescribeFailure(req));
                    yield break;
                }

                ProblemListDto list = null;
                try
                {
                    list = JsonUtility.FromJson<ProblemListDto>(req.downloadHandler.text);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[API] 解析问题列表失败: " + e.Message);
                }

                if (list == null)
                {
                    onError?.Invoke("后端返回的列表无法解析");
                    yield break;
                }

                onSuccess?.Invoke(list.items ?? new ProblemDto[0]);
            }
        }

        static ProblemDto ParseProblem(string text)
        {
            try
            {
                return JsonUtility.FromJson<ProblemDto>(text);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[API] 解析问题详情失败: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// 把 UnityWebRequest 的失败翻译成用户能理解的原因。
        /// 这一层是"网络失败要有基本处理"的落点：绝不把裸 error 字符串丢给用户。
        /// </summary>
        static string DescribeFailure(UnityWebRequest req)
        {
            if (req.result == UnityWebRequest.Result.ConnectionError)
            {
                // 只留 host:port，不贴完整 URL：表单里的空间要留给"怎么办"，而不是"请求了哪个路径"
                return "无法连接后端 " + GetAuthority(req.url) + "，请确认 Go 服务已启动。";
            }

            string body = req.downloadHandler != null ? req.downloadHandler.text : null;
            if (!string.IsNullOrEmpty(body))
            {
                try
                {
                    ErrorDto err = JsonUtility.FromJson<ErrorDto>(body);
                    if (err != null && !string.IsNullOrEmpty(err.message))
                    {
                        return "后端拒绝（HTTP " + req.responseCode + "）：" + err.message;
                    }
                }
                catch (Exception)
                {
                    // 后端返回的不是标准错误体，退回到通用提示
                }
            }

            return "请求失败（HTTP " + req.responseCode + "）：" + req.error;
        }

        static string GetAuthority(string url)
        {
            try
            {
                return new Uri(url).Authority;
            }
            catch (Exception)
            {
                return url;
            }
        }
    }
}
