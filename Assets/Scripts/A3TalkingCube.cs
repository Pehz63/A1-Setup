using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

/// <summary>
/// Talking Cube — Speech → Text → LLM → Speech, through the UMN AI Gateway.
///
/// Each step is one web request to the gateway (it speaks the OpenAI API):
///   Speech → Text   POST /v1/audio/transcriptions
///   Text → LLM      POST /v1/chat/completions
///   Text → Speech   POST /v1/audio/speech
/// Sending requests and converting audio are done for you in AIGatewayHelpers.cs.
///
/// Call BeginListening() on button-down and StopListening() on button-up.
///
/// Visual feedback:
///   White  = Idle
///   Red    = Recording
///   Yellow = Processing
///   Green  = Speaking
/// </summary>
public class A3TalkingCube : MonoBehaviour
{
    [Header("UMN AI Gateway")]
    [SerializeField] private string gatewayBaseUrl = "https://api.aigateway.umn.edu";
    [Tooltip("Your UMN AI Gateway key (starts with sk-). It is saved in the scene file, so don't push it to a public repo.")]
    [SerializeField] private string apiKey = "<your-api-key>";

    [Header("Models — one per step")]
    [SerializeField] private string sttModel = "gemini-3.5-transcribe-preview";
    [SerializeField] private string chatModel = "gpt-6-luna";
    [SerializeField] private string ttsModel = "azure-tts";
    [SerializeField] private string ttsVoice = "alloy";
    [SerializeField] private string systemPrompt = "You are a friendly talking cube for University of Minnesota's AI-enabled MR class. Your replies are spoken aloud, so don't use markdown or emoji.";

    [Header("Recording")]
    [SerializeField] private int maxRecordingSeconds = 15;
    [SerializeField] private int sampleRate = 16000;

    [Header("Visual Feedback")]
    [SerializeField] private Color idleColor = Color.white;
    [SerializeField] private Color recordingColor = Color.red;
    [SerializeField] private Color processingColor = Color.yellow;
    [SerializeField] private Color speakingColor = Color.green;

    // ── State ────────────────────────────────────────────────────────────────
    private enum State { Idle, Recording, Processing, Speaking }
    private State currentState = State.Idle;

    // ── One client for all three models ──────────────────────────────────────
    private AIGatewayClient gateway;

    // ── Components ───────────────────────────────────────────────────────────
    private AudioSource audioSource;
    private Renderer cubeRenderer;
    private MaterialPropertyBlock propBlock;

    // ── Recording ────────────────────────────────────────────────────────────
    private string micDevice;
    private AudioClip recordingClip;

    // ── Conversation memory: a list of chat messages ─────────────────────────
    // Each message is a small JSON object, e.g. { "role": "user", "content": "Hello!" }
    private readonly JArray history = new JArray();
    private CancellationTokenSource cts;

    void Start()
    {
        // The gateway client is already set up for you (do not change unless instructed).
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Trim().StartsWith("<"))
        {
            Debug.LogError("[TalkingCube] Paste your gateway API key into the \"Api Key\" field in the Inspector.");
            enabled = false;
            return;
        }
        gateway = new AIGatewayClient(gatewayBaseUrl, apiKey.Trim());

        // Unity components
        audioSource = gameObject.AddComponent<AudioSource>();
        cubeRenderer = GetComponent<Renderer>();
        propBlock = new MaterialPropertyBlock();

        if (Microphone.devices.Length > 0)
            micDevice = Microphone.devices[0];
        else
            Debug.LogWarning("[TalkingCube] No microphone found.");

        history.Add(new JObject { ["role"] = "system", ["content"] = systemPrompt });

        // TODO (Part A): ensure the cube starts in the correct color.
        ApplyColor();
    }

    /// <summary>Call this when the user presses the button.</summary>
    [ContextMenu("Begin Listening")]
    public void BeginListening()
    {
        if (micDevice == null || currentState != State.Idle) return;
        recordingClip = Microphone.Start(micDevice, loop: false, maxRecordingSeconds, sampleRate);

        // Recording state should show red
        SetState(State.Recording);
    }

    /// <summary>Call this when the user releases the button.</summary>
    [ContextMenu("Stop Listening")]
    public void StopListening()
    {
        if (currentState != State.Recording) return;

        // If the button was held longer than maxRecordingSeconds, the mic has already stopped by itself.
        int capturedSamples = Microphone.IsRecording(micDevice)
            ? Microphone.GetPosition(micDevice)
            : recordingClip.samples;
        Microphone.End(micDevice);

        if (capturedSamples < recordingClip.frequency / 4)
        {
            Debug.Log("[TalkingCube] Recording too short, ignoring.");
            SetState(State.Idle);
            return;
        }

        // Processing state should show yellow
        SetState(State.Processing);

        cts = new CancellationTokenSource();
        _ = RunPipelineAsync(recordingClip, capturedSamples, cts.Token);
    }

    // ── Pipeline (Part B) ─────────────────────────────────────────────────────
    async Task RunPipelineAsync(AudioClip clip, int capturedSamples, CancellationToken token)
    {
        try
        {
            // TODO (Part B.1): Speech → Text
            byte[] wavBytes = WavUtility.FromAudioClip(clip, capturedSamples); // the recording as a WAV file

            // - Build a form with three fields:
            //     new MultipartFormDataSection("model", sttModel)
            //     new MultipartFormDataSection("language", "en")
            //     new MultipartFormFileSection("file", wavBytes, "recording.wav", "audio/wav")
            //   (put them in a List<IMultipartFormSection>)
            // - Send it: await gateway.PostFormAsync("/v1/audio/transcriptions", form, token)
            // - The reply is JSON like { "text": "what you said" }
            // - Store the text in: string userText
            IMultipartFormSection model = new MultipartFormDataSection("model", sttModel);
            IMultipartFormSection language = new MultipartFormDataSection("language", "en");
            IMultipartFormSection file = new MultipartFormFileSection("file", wavBytes, "recording.wav", "audio/wav");
            List<IMultipartFormSection> list = new() { model, language, file };

            JObject sttResponse = await gateway.PostFormAsync("/v1/audio/transcriptions", list, token);
            string userText = (string)sttResponse["text"];

            // TODO: If userText is empty/whitespace:
            // - Log "No speech detected."
            // - SetState(State.Idle)
            // - return
            if (string.IsNullOrWhiteSpace(userText))
            {
                Debug.Log("[TalkingCube] No speech detected.");
                SetState(State.Idle);
                return;
            }

            // TODO (Part B.2): Text → LLM
            // - Add the user's words to history:  { "role": "user", "content": userText }
            // - Build the request body:           { "model": chatModel, "messages": history }
            // - Send it: await gateway.PostJsonAsync("/v1/chat/completions", body, token)
            // - The reply text is at response["choices"][0]["message"]["content"]
            // - Add the reply to history:          { "role": "assistant", "content": reply }

            history.Add(new JObject { ["role"] = "user", ["content"] = userText });
            JObject chatBody = new JObject { ["model"] = chatModel, ["messages"] = history };
            JObject chatResponse = await gateway.PostJsonAsync("/v1/chat/completions", chatBody, token);
            string chatReply = AIGatewayClient.ContentAsText(chatResponse["choices"][0]["message"]["content"]);
            history.Add(new JObject { ["role"] = "assistant", ["content"] = chatReply });

            // TODO (Part B.3): Text → Speech
            // - Build the request body: { "model": ttsModel, "input": reply, "voice": ttsVoice, "response_format": "wav" }
            // - Send it: await gateway.PostJsonForBytesAsync("/v1/audio/speech", body, token)  → WAV bytes
            // - Turn the bytes into a clip: WavUtility.ToAudioClip(bytes)
            // - Store in AudioClip speechClip

            JObject speechBody = new JObject {
                ["model"] = ttsModel,
                ["input"] = chatReply,
                ["voice"] = ttsVoice,
                ["response_format"] = "wav"
            };
            byte[] speechResponse = await gateway.PostJsonForBytesAsync("/v1/audio/speech", speechBody, token);
            AudioClip speechClip = WavUtility.ToAudioClip(speechResponse);

            // TODO (Part B.4): Play + state transitions
            // - SetState(State.Speaking)
            // - audioSource.PlayOneShot(speechClip)
            // - await Task.Delay( lengthInMs + smallPadding, token )
            // - SetState(State.Idle)
            SetState(State.Speaking);
            audioSource.PlayOneShot(speechClip);
            await Task.Delay((int)(speechClip.length*1000 + 50), token );
            SetState(State.Idle);
        }
        catch (OperationCanceledException)
        {
            // TODO: Return to Idle if canceled
            SetState(State.Idle);
        }
        catch (Exception ex)
        {
            // Common gateway errors: 401 bad key, 403 model not allowed for your key, 429 rate or budget limit.
            Debug.LogError($"[TalkingCube] Error: {ex.Message}");
            // TODO: Return to Idle on error
            SetState(State.Idle);
        }
    }

    // ── State + Visual Feedback (Part A) ──────────────────────────────────────
    void SetState(State newState)
    {
        // TODO (Part A.1):
        // - Update currentState
        // - Call ApplyColor()
        currentState = newState;
        ApplyColor();
    }

    void ApplyColor()
    {
        // TODO (Part A.2):
        // - If cubeRenderer is null, return
        // - Pick the correct Color based on currentState:
        //     Idle       -> idleColor
        //     Recording  -> recordingColor
        //     Processing -> processingColor
        //     Speaking   -> speakingColor
        //
        // - Use MaterialPropertyBlock to set "_BaseColor"
        //   (GetPropertyBlock, SetColor, SetPropertyBlock)
        if (cubeRenderer is null) {
            return;
        }

        Color color = Color.white; // chosen by the switch below, then applied to the cube
        switch (currentState)
        {
            case State.Idle:
                color = idleColor;
                break;
            case State.Recording:
                color = recordingColor;
                break;
            case State.Processing:
                color = processingColor;
                break;
            case State.Speaking:
                color = speakingColor;
                break;
        }

        cubeRenderer.GetPropertyBlock(propBlock);
        propBlock.SetColor("_BaseColor", color);
        cubeRenderer.SetPropertyBlock(propBlock);

    }

    void OnDestroy()
    {
        cts?.Cancel();
        cts?.Dispose();
    }
}
