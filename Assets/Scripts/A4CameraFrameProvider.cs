using System.Threading.Tasks;
using Meta.XR;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// Captures frames from PassthroughCameraAccess and provides them as Texture2D.
/// Also displays a live preview on a debug canvas.
/// </summary>
public class A4CameraFrameProvider : MonoBehaviour
{
    [SerializeField] private PassthroughCameraAccess passthroughCamera;
    [SerializeField] private RawImage debugPreview;

    private Texture2D _capturedTexture;

    void Update()
    {
        // Update the debug preview every frame the camera has new data
        if (passthroughCamera != null
            && passthroughCamera.IsPlaying
            && passthroughCamera.IsUpdatedThisFrame
            && debugPreview != null)
        {
            debugPreview.texture = passthroughCamera.GetTexture();
        }
    }

    /// <summary>
    /// Captures the current camera frame as a Texture2D using async GPU readback.
    /// Returns null if the camera is not playing or readback fails.
    /// </summary>
    public async Task<Texture2D> CaptureFrameAsync()
    {
        // TODO: Check that passthroughCamera is not null and IsPlaying.
        //       If not, log a warning and return null.
        if (passthroughCamera == null)
        {
            Debug.LogWarning("[A4CameraFrameProvider] Passthrough camera is null");
            return null;
        }
        if (!passthroughCamera.IsPlaying)
        {
            Debug.LogWarning("[A4CameraFrameProvider] Passthrough camera is not playing");
            return null;
        }

        // TODO: Call passthroughCamera.GetTexture() and cast it to RenderTexture.
        //       If the cast fails (null), return null.
        Texture cameraTexture = passthroughCamera.GetTexture();

        // In the Unity Editor (Play mode / Quest Link) the camera gives a plain Texture2D, not a RenderTexture.
        // It is already CPU-readable, so copy its pixels directly instead of using GPU readback.
        if (cameraTexture is Texture2D cameraTexture2D)
        {
            var pixels = cameraTexture2D.GetPixelData<Color32>(0);
            if (_capturedTexture == null
                || _capturedTexture.width != cameraTexture2D.width
                || _capturedTexture.height != cameraTexture2D.height)
            {
                _capturedTexture = new Texture2D(cameraTexture2D.width, cameraTexture2D.height, TextureFormat.RGBA32, false);
            }
            _capturedTexture.SetPixelData(pixels, 0);
            _capturedTexture.Apply();
            return _capturedTexture;
        }

        RenderTexture rt = cameraTexture as RenderTexture;
        if (rt == null)
        {
            Debug.LogWarning("[A4CameraFrameProvider] Camera texture is neither a Texture2D nor a RenderTexture");
            return null;
        }

        // TODO: Issue an async GPU readback using AsyncGPUReadback.Request(rt).
        //       Wait for it to complete WITHOUT blocking the main thread.
        //       Hint: use a while loop with `await Task.Yield()`.
        var request = AsyncGPUReadback.Request(rt);
        while (!request.done)
        {
            await Task.Yield();
        }

        // TODO: If the request has an error, log it and return null.
        if (request.hasError)
        {
            Debug.LogError($"[A4CameraFrameProvider] GPU readback failed. Texture: {rt.width}x{rt.height}, format: {rt.graphicsFormat}");
            return null;
        }

        // TODO: Read the pixel data from the request using request.GetData<Color32>().
        //       Create (or reuse) a Texture2D at the correct resolution,
        //       copy the pixel data in with SetPixelData, and call Apply().
        var data = request.GetData<Color32>();
        // make sure _capturedTexture is correct:
        if (_capturedTexture == null
            || _capturedTexture.width != rt.width
            || _capturedTexture.height != rt.height)
        {
            _capturedTexture = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        }
        _capturedTexture.SetPixelData(data, 0);
        _capturedTexture.Apply();

        // TODO: Return the Texture2D.
        return _capturedTexture;
    }
}