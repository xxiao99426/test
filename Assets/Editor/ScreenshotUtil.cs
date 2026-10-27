#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace InspectionDemo.EditorTools
{
    /// <summary>
    /// 自己渲染一帧并写 PNG，包含 Screen Space Overlay 的 UI。
    ///
    /// 为什么需要它：Unity 编辑器窗口失焦时 Game View 不再重绘，
    /// 常规截图接口返回的是上一帧缓存，改了 UI 也看不出来。
    /// 这里把 Canvas 临时切成 Screen Space Camera，用一个正交 UI 相机把场景和 UI
    /// 依次渲染进同一张 RenderTexture，绕过「窗口不重绘」这个限制。
    ///
    /// 只用于开发期自查，不参与运行时逻辑，也不会被打进包。
    /// </summary>
    public static class ScreenshotUtil
    {
        const int UICameraLayer = 5; // 内置 UI 层

        [MenuItem("巡检工具/截图（含 UI）", false, 30)]
        public static void CaptureMenuItem()
        {
            string name = "capture_" + System.DateTime.Now.ToString("HHmmss") + ".png";
            string result = Capture(name);
            Debug.Log("[Shot] " + result);
        }

        public static string Capture(string fileName)
        {
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
            Camera main = Camera.main;

            if (canvas == null || main == null)
            {
                return "失败：找不到 Canvas 或 Main Camera";
            }

            int width = Mathf.Max(320, Screen.width);
            int height = Mathf.Max(240, Screen.height);

            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Captures"));
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, fileName);

            RenderMode oldMode = canvas.renderMode;
            Camera oldWorldCamera = canvas.worldCamera;
            float oldPlaneDistance = canvas.planeDistance;
            RenderTexture oldMainTarget = main.targetTexture;
            RenderTexture oldActive = RenderTexture.active;

            Transform[] nodes = canvas.GetComponentsInChildren<Transform>(true);
            int[] oldLayers = new int[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                oldLayers[i] = nodes[i].gameObject.layer;
                nodes[i].gameObject.layer = UICameraLayer;
            }

            GameObject uiCamGo = new GameObject("__ScreenshotUICamera");
            uiCamGo.hideFlags = HideFlags.HideAndDontSave;
            Camera uiCam = uiCamGo.AddComponent<Camera>();
            uiCam.clearFlags = CameraClearFlags.Depth;
            uiCam.cullingMask = 1 << UICameraLayer;
            uiCam.orthographic = true;
            uiCam.orthographicSize = height * 0.5f;
            uiCam.nearClipPlane = 0.1f;
            uiCam.farClipPlane = 20f;
            uiCam.depth = main.depth + 10;
            uiCam.allowHDR = false;

            RenderTexture rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            try
            {
                main.targetTexture = rt;
                main.Render();

                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = uiCam;
                canvas.planeDistance = 1f;

                uiCam.targetTexture = rt;
                uiCam.Render();

                RenderTexture.active = rt;
                Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                tex.Apply();
                RenderTexture.active = oldActive;

                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            finally
            {
                // 无论成功失败都要把场景恢复原样，否则一次截图会把运行时 UI 改坏
                canvas.renderMode = oldMode;
                canvas.worldCamera = oldWorldCamera;
                canvas.planeDistance = oldPlaneDistance;
                main.targetTexture = oldMainTarget;
                uiCam.targetTexture = null;
                RenderTexture.active = oldActive;
                RenderTexture.ReleaseTemporary(rt);

                for (int i = 0; i < nodes.Length; i++)
                {
                    if (nodes[i] != null) nodes[i].gameObject.layer = oldLayers[i];
                }
                Object.DestroyImmediate(uiCamGo);
            }

            AssetDatabase.Refresh();
            return string.Format("成功：{0}（{1}x{2}）", path, width, height);
        }
    }
}
#endif
