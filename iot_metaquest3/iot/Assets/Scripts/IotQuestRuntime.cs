using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Hands;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class IotQuestRuntime : MonoBehaviour
{
    private const string LogTag = "CASA_SEGURA_QUEST";
    private const string LogPrefix = "[CASA_SEGURA_QUEST] ";
    private const int DiscoveryTimeoutMs = 2000;
    private const int BackendHttpPort = 8080;
    private const int SubnetProbeTimeoutMs = 700;
    private const int SubnetProbeBatchSize = 20;
    private const float WristButtonPressDistance = 0.07f;
    private const float WristButtonPinchDistance = 0.04f;
    private const float WristButtonCooldownSeconds = 0.45f;
    private static readonly Color HudGlow = new(0.17f, 0.92f, 1f, 0.92f);
    private static readonly Color HudGlowDim = new(0.08f, 0.36f, 0.46f, 0.75f);
    private static readonly Color HudPanel = new(0.02f, 0.05f, 0.09f, 0.72f);
    private static readonly Color FanOnColor = new(1f, 0.45f, 0.15f, 1f);
    private static readonly Color FanOffColor = new(0.45f, 0.52f, 0.58f, 0.9f);
    private static readonly string[] TemplateRootNames = Array.Empty<string>();
    private static readonly string[] TemplateNameTokens = { "Tooltip", "Affordance", "Tutorial", "Coaching", "Spatial Panel Manipulator", "Quest Settings Panel", "Snap Volume", "Learn Modal", "Skip Button" };
    private static readonly List<XRHandSubsystem> HandSubsystems = new();

    private TextMeshProUGUI temperatureValue;
    private TextMeshProUGUI humidityValue;
    private TextMeshProUGUI pressureValue;
    private TextMeshProUGUI gasValue;
    private TextMeshProUGUI wifiValue;
    private TextMeshProUGUI modeValue;
    private TextMeshProUGUI fanValue;
    private TextMeshProUGUI statusValue;
    private TextMeshProUGUI backendValue;
    private Image fanIndicator;
    private Transform hudAnchor;
    private Transform wristButtonRoot;
    private Renderer wristButtonRenderer;
    private TextMesh wristButtonLabel;
    private XRHandSubsystem handSubsystem;

    private const string DiscoveryMessage = "CASA_SEGURA_DISCOVER";
    private const string DiscoveryResponsePrefix = "IOT_BACKEND ";
    private const string BackendBaseUrlPrefKey = "CasaSegura.BackendBaseUrl";

    [SerializeField] private string backendBaseUrl = "";
    [SerializeField] private string deviceCode = "esp32-001";
    [SerializeField] private float refreshIntervalSeconds = 0.5f;
    [SerializeField] private float discoveryRetrySeconds = 3f;
    [SerializeField] private int discoveryPort = 8266;
    [SerializeField] private bool autoDiscoverBackend = true;

    private bool fanEnabled;
    private bool relayEnabled;
    private bool previousPrimaryButton;
    private bool requestInFlight;
    private bool discoveryInFlight;
    private string lastStatusLine = "BUSCANDO BACKEND EN LAN...";
    private string lastBackendLabel = "SIN BACKEND";
    private int knownDeviceCount;
    private float nextWristToggleAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void LogBootstrap()
    {
        LogInfo("Bootstrap BeforeSceneLoad.");
    }

    private void Awake()
    {
        LogInfo("Awake. scene='" + gameObject.scene.name + "' enabled=" + isActiveAndEnabled + ".");
        PruneTemplateScene();
        ResolveHandSubsystem();
        BuildWristButton();
    }

    private void OnEnable()
    {
        LogInfo("OnEnable runtime Quest.");
    }

    private void Start()
    {
        backendBaseUrl = ResolveInitialBackendBaseUrl();
        LogInfo("Inicio runtime. backendBaseUrl inicial='" + backendBaseUrl + "' deviceCode='" + deviceCode + "'.");
        BuildHud();
        UpdateWristButtonVisual();
        UpdateHud(CreateOfflineSnapshot(lastStatusLine));
        StartCoroutine(PollDashboardLoop());
    }

    private void Update()
    {
        HandlePrimaryButton();
        UpdateWristButton();
    }

    private void HandlePrimaryButton()
    {
        var rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        bool pressed = false;
        var hasPrimary = rightHand.isValid && rightHand.TryGetFeatureValue(CommonUsages.primaryButton, out pressed);
        if (!hasPrimary)
        {
            previousPrimaryButton = false;
            return;
        }

        if (pressed && !previousPrimaryButton)
        {
            TriggerFanToggle("controller-a");
        }

        previousPrimaryButton = pressed;
    }

    private IEnumerator PollDashboardLoop()
    {
        while (true)
        {
            if (string.IsNullOrWhiteSpace(backendBaseUrl))
            {
                yield return DiscoverBackend();
                yield return new WaitForSeconds(discoveryRetrySeconds);
                continue;
            }

            yield return FetchDashboardFeed();
            yield return new WaitForSeconds(refreshIntervalSeconds);
        }
    }

    private IEnumerator DiscoverBackend()
    {
        if (!autoDiscoverBackend || discoveryInFlight)
        {
            yield break;
        }

        discoveryInFlight = true;
        lastStatusLine = "BUSCANDO BACKEND EN LAN...";
        LogInfo("Intentando descubrir backend por UDP en puerto " + discoveryPort + ".");
        UpdateHud(CreateOfflineSnapshot(lastStatusLine));

        NetworkDiscoveryContext networkContext = GetNetworkDiscoveryContext();
        LogInfo("Contexto de red Quest: " + networkContext.ToSummary());

        var discoveryTask = DiscoverBackendBaseUrlAsync(networkContext);
        while (!discoveryTask.IsCompleted)
        {
            yield return null;
        }

        discoveryInFlight = false;
        DiscoveryAttemptResult discoveryResult = discoveryTask.IsCompletedSuccessfully
            ? discoveryTask.Result
            : new DiscoveryAttemptResult(string.Empty, "none");
        string discoveredUrl = discoveryResult.BackendBaseUrl;
        if (string.IsNullOrWhiteSpace(discoveredUrl))
        {
            lastBackendLabel = "SIN BACKEND";
            lastStatusLine = "NO SE ENCONTRO BACKEND EN LA LAN";
            LogWarning("No se encontro backend por UDP ni por escaneo HTTP. " + networkContext.ToSummary());
            UpdateHud(CreateOfflineSnapshot(lastStatusLine));
            yield break;
        }

        backendBaseUrl = discoveredUrl;
        lastBackendLabel = backendBaseUrl;
        LogInfo("Backend descubierto por " + discoveryResult.Method + ": " + backendBaseUrl);
        PlayerPrefs.SetString(BackendBaseUrlPrefKey, backendBaseUrl);
        PlayerPrefs.Save();
        lastStatusLine = "BACKEND DESCUBIERTO";
        UpdateHud(CreateOfflineSnapshot(lastStatusLine));
    }

    private IEnumerator FetchDashboardFeed()
    {
        requestInFlight = true;
        string url = backendBaseUrl.TrimEnd('/') + "/api/device/readings";
        LogInfo("GET " + url);
        var requestTask = FetchDashboardJsonAsync(url);
        while (!requestTask.IsCompleted)
        {
            yield return null;
        }

        if (requestTask.IsFaulted)
        {
            LogWarning("Fallo GET lecturas. exception='" + requestTask.Exception?.GetBaseException().Message + "'.");
            lastBackendLabel = "BACKEND SIN RESPUESTA";
            lastStatusLine = "SIN ENLACE CON BACKEND";
            UpdateHud(CreateOfflineSnapshot(lastStatusLine));
            requestInFlight = false;
            backendBaseUrl = string.Empty;
            if (autoDiscoverBackend)
            {
                yield return DiscoverBackend();
            }
            yield break;
        }

        string responseJson = requestTask.Result;
        var payloads = ParseDashboardPayloads(responseJson);
        LogInfo("GET lecturas OK. dispositivos recibidos=" + payloads.Length + ".");
        knownDeviceCount = payloads.Length;
        var payload = ResolveActivePayload(payloads);
        if (payload == null)
        {
            lastBackendLabel = backendBaseUrl;
            lastStatusLine = "BACKEND OK, SIN LECTURAS";
            LogWarning("El backend respondio pero no hay lecturas en /api/device/readings.");
            UpdateHud(CreateOfflineSnapshot(lastStatusLine));
            requestInFlight = false;
            yield break;
        }

        LogInfo("Payload activo: deviceCode='" + payload.deviceCode + "' status='" + payload.status + "' temp=" + payload.temperatureC + " gas=" + payload.gasLevel + ".");
        fanEnabled = payload.fanOverrideEnabled;
        relayEnabled = payload.relayOn;
        lastBackendLabel = backendBaseUrl;
        lastStatusLine = payload.status == "online"
            ? BuildLiveStatusLine(payload)
            : "DISPOSITIVO SIN REPORTE RECIENTE";
        UpdateHud(new TelemetrySnapshot
        {
            TemperatureC = payload.temperatureC,
            Humidity = payload.humidity,
            PressureHpa = payload.pressureHpa,
            GasLevel = payload.gasLevel,
            WifiConnected = payload.wifiConnected,
            WifiLabel = payload.wifiConnected ? payload.deviceName : "SIN ENLACE",
            ModeLabel = BuildModeLabel(payload),
            FanLabel = BuildFanLabel(payload),
            FanEnabled = payload.relayOn,
            StatusLine = lastStatusLine,
            BackendLabel = BuildBackendLabel(),
        });
        requestInFlight = false;
    }

    private IEnumerator SendFanOverride(bool enabled)
    {
        if (string.IsNullOrWhiteSpace(backendBaseUrl))
        {
            fanEnabled = !enabled;
            lastStatusLine = "SIN BACKEND PARA ENVIAR COMANDO";
            LogWarning("Se intento enviar comando sin backend descubierto.");
            UpdateHud(CreateOfflineSnapshot(lastStatusLine));
            yield break;
        }

        requestInFlight = true;
        var payload = JsonUtility.ToJson(new FanOverridePayload { deviceCode = deviceCode, enabled = enabled });
        LogInfo("POST override ventilador a " + backendBaseUrl.TrimEnd('/') + "/api/device/fan payload=" + payload);
        var requestTask = PostFanOverrideAsync(backendBaseUrl.TrimEnd('/') + "/api/device/fan", payload);
        while (!requestTask.IsCompleted)
        {
            yield return null;
        }

        if (requestTask.IsFaulted)
        {
            LogWarning("Fallo POST override. exception='" + requestTask.Exception?.GetBaseException().Message + "'.");
            lastStatusLine = "NO SE PUDO ENVIAR EL COMANDO";
            UpdateHud(CreateOfflineSnapshot(lastStatusLine));
            requestInFlight = false;
            yield break;
        }

        var response = JsonUtility.FromJson<FanOverrideResponsePayload>(requestTask.Result);
        LogInfo("POST override OK. enabled=" + response.enabled + " message='" + response.message + "'.");
        fanEnabled = response.enabled;
        relayEnabled = relayEnabled || response.enabled;
        lastStatusLine = response.message;
        requestInFlight = false;
        yield return FetchDashboardFeed();
    }

    private TelemetrySnapshot CreateOfflineSnapshot(string status)
    {
        return new TelemetrySnapshot
        {
            TemperatureC = 0f,
            Humidity = 0f,
            PressureHpa = 0f,
            GasLevel = 0f,
            WifiConnected = false,
            WifiLabel = "BACKEND DESCONECTADO",
            ModeLabel = fanEnabled ? "VENTILACION MANUAL" : relayEnabled ? "VENTILADOR ACTIVO" : "ESPERANDO ENLACE",
            FanLabel = fanEnabled ? "MANUAL" : relayEnabled ? "ENCENDIDO" : "APAGADO",
            FanEnabled = relayEnabled,
            StatusLine = status,
            BackendLabel = BuildBackendLabel(),
        };
    }

    private void UpdateHud(TelemetrySnapshot snapshot)
    {
        if (temperatureValue == null)
        {
            return;
        }

        temperatureValue.text = snapshot.TemperatureC.ToString("0.0") + " C";
        humidityValue.text = snapshot.Humidity.ToString("0") + " %";
        pressureValue.text = snapshot.PressureHpa.ToString("0") + " hPa";
        gasValue.text = snapshot.GasLevel.ToString("0") + " %";
        wifiValue.text = snapshot.WifiConnected ? snapshot.WifiLabel : "SIN ENLACE";
        modeValue.text = snapshot.ModeLabel;
        fanValue.text = snapshot.FanLabel;
        statusValue.text = snapshot.StatusLine;
        backendValue.text = snapshot.BackendLabel;
        fanIndicator.color = snapshot.FanEnabled ? FanOnColor : FanOffColor;
        fanValue.color = snapshot.FanEnabled ? FanOnColor : HudGlow;
        UpdateWristButtonVisual();
    }

    private void BuildHud()
    {
        hudAnchor = ResolveHudAnchor();
        if (hudAnchor == null)
        {
            LogWarning("No se encontro una camara XR para montar el HUD.");
            return;
        }

        var canvasObject = new GameObject("IronHudCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(hudAnchor, false);

        var canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.localPosition = new Vector3(0f, 0f, 1.08f);
        canvasRect.localRotation = Quaternion.identity;
        canvasRect.localScale = Vector3.one * 0.00115f;
        canvasRect.sizeDelta = new Vector2(1220f, 720f);

        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = hudAnchor.GetComponent<Camera>();
        canvas.sortingOrder = 100;

        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 12f;

        canvasObject.GetComponent<GraphicRaycaster>().enabled = false;

        var rootPanel = CreatePanel("RootPanel", canvasRect, new Vector2(1220f, 720f), HudPanel, new Vector2(0.5f, 0.5f), Vector2.zero);
        CreateLine(rootPanel, new Vector2(540f, 300f), new Vector2(170f, 4f), HudGlow);
        CreateLine(rootPanel, new Vector2(-540f, 300f), new Vector2(170f, 4f), HudGlow);
        CreateLine(rootPanel, new Vector2(0f, -310f), new Vector2(920f, 2f), HudGlowDim);
        CreateLine(rootPanel, new Vector2(0f, 220f), new Vector2(1040f, 2f), HudGlowDim);

        CreateText(rootPanel, "CASA SEGURA // VISION HUD", 42, FontStyles.Bold, HudGlow, TextAlignmentOptions.Center, new Vector2(0f, 286f), new Vector2(900f, 64f));
        CreateText(rootPanel, "META QUEST 3 // STREAM DE TELEMETRIA", 20, FontStyles.UpperCase, new Color(0.7f, 0.96f, 1f, 0.86f), TextAlignmentOptions.Center, new Vector2(0f, 246f), new Vector2(760f, 32f));

        var leftColumn = CreatePanel("LeftColumn", rootPanel, new Vector2(540f, 380f), new Color(0.04f, 0.11f, 0.16f, 0.56f), new Vector2(0.5f, 0.5f), new Vector2(-270f, 10f));
        var rightColumn = CreatePanel("RightColumn", rootPanel, new Vector2(520f, 380f), new Color(0.04f, 0.11f, 0.16f, 0.56f), new Vector2(0.5f, 0.5f), new Vector2(280f, 10f));

        temperatureValue = CreateMetric(leftColumn, "TEMPERATURA", new Vector2(-120f, 118f));
        humidityValue = CreateMetric(leftColumn, "HUMEDAD", new Vector2(120f, 118f));
        pressureValue = CreateMetric(leftColumn, "PRESION", new Vector2(-120f, -32f));
        gasValue = CreateMetric(leftColumn, "GAS", new Vector2(120f, -32f));

        wifiValue = CreateLabelValue(rightColumn, "ENLACE", new Vector2(0f, 118f));
        modeValue = CreateLabelValue(rightColumn, "MODO", new Vector2(0f, 32f));
        fanValue = CreateLabelValue(rightColumn, "VENTILADOR", new Vector2(0f, -54f));

        var fanBadge = CreatePanel("FanBadge", rightColumn, new Vector2(78f, 78f), new Color(0.08f, 0.16f, 0.2f, 0.95f), new Vector2(0.5f, 0.5f), new Vector2(0f, -146f));
        fanIndicator = fanBadge.GetComponent<Image>();
        CreateText(fanBadge, "A", 34, FontStyles.Bold, Color.white, TextAlignmentOptions.Center, Vector2.zero, new Vector2(60f, 60f));
        CreateText(rightColumn, "BOTON A O MUNECA // OVERRIDE MANUAL", 18, FontStyles.Bold, HudGlow, TextAlignmentOptions.Center, new Vector2(0f, -214f), new Vector2(420f, 28f));

        statusValue = CreateText(rootPanel, string.Empty, 19, FontStyles.Bold, HudGlow, TextAlignmentOptions.Center, new Vector2(0f, -272f), new Vector2(1020f, 32f));
        backendValue = CreateText(rootPanel, string.Empty, 17, FontStyles.Bold, new Color(0.74f, 0.92f, 1f, 0.8f), TextAlignmentOptions.Center, new Vector2(0f, -300f), new Vector2(1080f, 26f));
    }

    private string ResolveInitialBackendBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(backendBaseUrl))
        {
            return backendBaseUrl.Trim();
        }

        return PlayerPrefs.GetString(BackendBaseUrlPrefKey, string.Empty).Trim();
    }

    private void ResolveHandSubsystem()
    {
        if (handSubsystem != null && handSubsystem.running)
        {
            return;
        }

        handSubsystem = null;
        HandSubsystems.Clear();
        SubsystemManager.GetSubsystems(HandSubsystems);
        for (int index = 0; index < HandSubsystems.Count; index++)
        {
            if (HandSubsystems[index] != null && HandSubsystems[index].running)
            {
                handSubsystem = HandSubsystems[index];
                LogInfo("XR Hands detectado.");
                return;
            }
        }
    }

    private void BuildWristButton()
    {
        if (wristButtonRoot != null)
        {
            return;
        }

        wristButtonRoot = new GameObject("WristFanButton").transform;
        wristButtonRoot.gameObject.SetActive(false);

        var buttonVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        buttonVisual.name = "ButtonVisual";
        buttonVisual.transform.SetParent(wristButtonRoot, false);
        buttonVisual.transform.localScale = new Vector3(0.08f, 0.05f, 0.012f);
        var collider = buttonVisual.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }

        wristButtonRenderer = buttonVisual.GetComponent<Renderer>();
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        wristButtonRenderer.material = new Material(shader);

        var labelObject = new GameObject("ButtonLabel");
        labelObject.transform.SetParent(wristButtonRoot, false);
        labelObject.transform.localPosition = new Vector3(0f, 0f, 0.008f);
        labelObject.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        labelObject.transform.localScale = Vector3.one * 0.02f;
        wristButtonLabel = labelObject.AddComponent<TextMesh>();
        wristButtonLabel.anchor = TextAnchor.MiddleCenter;
        wristButtonLabel.alignment = TextAlignment.Center;
        wristButtonLabel.fontSize = 72;
        wristButtonLabel.characterSize = 0.055f;
        wristButtonLabel.color = Color.white;
    }

    private void UpdateWristButton()
    {
        ResolveHandSubsystem();
        if (handSubsystem == null)
        {
            SetWristButtonVisible(false);
            return;
        }

        bool hasLeftHand = TryGetTrackedHandState(handSubsystem.leftHand, true, out HandTrackingState leftHandState);
        bool hasRightHand = TryGetTrackedHandState(handSubsystem.rightHand, false, out HandTrackingState rightHandState);
        if (!hasLeftHand && !hasRightHand)
        {
            SetWristButtonVisible(false);
            return;
        }

        HandTrackingState anchorHandState = hasLeftHand ? leftHandState : rightHandState;
        UpdateWristButtonPose(anchorHandState);
        SetWristButtonVisible(true);
        UpdateWristButtonVisual();

        if (Time.unscaledTime < nextWristToggleAt)
        {
            return;
        }

        if (hasRightHand && rightHandState.IsPinching && Vector3.Distance(rightHandState.PinchPosition, wristButtonRoot.position) <= WristButtonPressDistance)
        {
            nextWristToggleAt = Time.unscaledTime + WristButtonCooldownSeconds;
            TriggerFanToggle("handtracking-right");
            return;
        }

        if (hasLeftHand && leftHandState.IsPinching && Vector3.Distance(leftHandState.PinchPosition, wristButtonRoot.position) <= WristButtonPressDistance)
        {
            nextWristToggleAt = Time.unscaledTime + WristButtonCooldownSeconds;
            TriggerFanToggle("handtracking-left");
        }
    }

    private bool TryGetTrackedHandState(XRHand hand, bool isLeftHand, out HandTrackingState handState)
    {
        handState = default;
        if (!hand.isTracked)
        {
            return false;
        }

        if (!TryGetJointPose(hand, XRHandJointID.Wrist, out handState.WristPose))
        {
            return false;
        }

        if (!TryGetJointPose(hand, XRHandJointID.Palm, out handState.PalmPose))
        {
            handState.PalmPose = handState.WristPose;
        }

        bool hasIndexTip = TryGetJointPose(hand, XRHandJointID.IndexTip, out handState.IndexTipPose);
        bool hasThumbTip = TryGetJointPose(hand, XRHandJointID.ThumbTip, out handState.ThumbTipPose);
        handState.IsLeftHand = isLeftHand;
        if (hasIndexTip && hasThumbTip)
        {
            handState.PinchPosition = Vector3.Lerp(handState.IndexTipPose.position, handState.ThumbTipPose.position, 0.5f);
            handState.IsPinching = Vector3.Distance(handState.IndexTipPose.position, handState.ThumbTipPose.position) <= WristButtonPinchDistance;
        }

        return true;
    }

    private static bool TryGetJointPose(XRHand hand, XRHandJointID jointId, out Pose pose)
    {
        var joint = hand.GetJoint(jointId);
        return joint.TryGetPose(out pose);
    }

    private void UpdateWristButtonPose(HandTrackingState handState)
    {
        if (wristButtonRoot == null)
        {
            return;
        }

        Vector3 wristForward = handState.WristPose.rotation * Vector3.forward;
        Vector3 wristRight = handState.WristPose.rotation * Vector3.right;
        Vector3 palmDirection = (handState.PalmPose.position - handState.WristPose.position).normalized;
        if (palmDirection.sqrMagnitude < 0.001f)
        {
            palmDirection = wristForward;
        }

        Transform viewer = hudAnchor != null ? hudAnchor : ResolveHudAnchor();
        Vector3 viewDirection = viewer != null ? (viewer.position - handState.WristPose.position).normalized : -wristForward;
        Vector3 viewerUp = viewer != null ? viewer.up : Vector3.up;
        Vector3 armDirection = -palmDirection;
        if (armDirection.sqrMagnitude < 0.001f)
        {
            armDirection = -wristForward;
        }

        Vector3 outwardDirection = handState.IsLeftHand ? -wristRight : wristRight;
        Vector3 buttonUp = Vector3.ProjectOnPlane(viewerUp, viewDirection).normalized;
        if (buttonUp.sqrMagnitude < 0.001f)
        {
            buttonUp = Vector3.up;
        }

        Vector3 buttonPosition = handState.WristPose.position + armDirection * 0.08f + outwardDirection * 0.055f + viewDirection * 0.03f;
        Quaternion buttonRotation = Quaternion.LookRotation(viewDirection, buttonUp);
        wristButtonRoot.SetPositionAndRotation(buttonPosition, buttonRotation);
    }

    private void SetWristButtonVisible(bool visible)
    {
        if (wristButtonRoot != null && wristButtonRoot.gameObject.activeSelf != visible)
        {
            wristButtonRoot.gameObject.SetActive(visible);
        }
    }

    private void UpdateWristButtonVisual()
    {
        if (wristButtonRenderer == null || wristButtonLabel == null)
        {
            return;
        }

        wristButtonRenderer.material.color = relayEnabled || fanEnabled ? FanOnColor : FanOffColor;
        wristButtonLabel.text = fanEnabled ? "MAN\nON" : relayEnabled ? "AUTO\nON" : "FAN\nOFF";
    }

    private void TriggerFanToggle(string source)
    {
        fanEnabled = !fanEnabled;
        relayEnabled = relayEnabled || fanEnabled;
        UpdateWristButtonVisual();
        LogInfo("Toggle ventilador desde " + source + ". nuevoEstado=" + fanEnabled);
        if (!requestInFlight)
        {
            StartCoroutine(SendFanOverride(fanEnabled));
        }
    }

    private void PruneTemplateScene()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        foreach (GameObject rootObject in activeScene.GetRootGameObjects())
        {
            if (rootObject == gameObject)
            {
                continue;
            }

            if (Array.IndexOf(TemplateRootNames, rootObject.name) >= 0)
            {
                LogInfo("Eliminando root de template: " + rootObject.name);
                Destroy(rootObject);
                continue;
            }

            RemoveTemplateChildren(rootObject.transform);
        }
    }

    private void RemoveTemplateChildren(Transform parent)
    {
        List<GameObject> objectsToHide = new();
        foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
        {
            if (child == null || child == transform)
            {
                continue;
            }

            if (ShouldDestroyTemplateObject(child.name))
            {
                objectsToHide.Add(child.gameObject);
            }
        }

        for (int index = 0; index < objectsToHide.Count; index++)
        {
            if (!objectsToHide[index].activeSelf)
            {
                continue;
            }

            LogInfo("Ocultando objeto de template: " + objectsToHide[index].name);
            objectsToHide[index].SetActive(false);
        }
    }

    private static bool ShouldDestroyTemplateObject(string objectName)
    {
        for (int index = 0; index < TemplateNameTokens.Length; index++)
        {
            if (objectName.IndexOf(TemplateNameTokens[index], StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<DiscoveryAttemptResult> DiscoverBackendBaseUrlAsync(NetworkDiscoveryContext networkContext)
    {
        if (!autoDiscoverBackend)
        {
            return new DiscoveryAttemptResult(string.Empty, "disabled");
        }

        string discoveredByUdp = await Task.Run(() => TryDiscoverBackendViaUdp(networkContext));
        if (!string.IsNullOrWhiteSpace(discoveredByUdp))
        {
            return new DiscoveryAttemptResult(discoveredByUdp, "udp");
        }

        string discoveredBySubnetScan = await DiscoverBackendViaHttpScanAsync(networkContext);
        if (!string.IsNullOrWhiteSpace(discoveredBySubnetScan))
        {
            return new DiscoveryAttemptResult(discoveredBySubnetScan, "http-scan");
        }

        return new DiscoveryAttemptResult(string.Empty, "none");
    }

    private string TryDiscoverBackendViaUdp(NetworkDiscoveryContext networkContext)
    {
        List<IPAddress> broadcastTargets = new() { IPAddress.Broadcast };
        if (IPAddress.TryParse(networkContext.BroadcastIp, out IPAddress broadcastAddress) && !broadcastTargets.Contains(broadcastAddress))
        {
            broadcastTargets.Add(broadcastAddress);
        }

        byte[] payload = Encoding.UTF8.GetBytes(DiscoveryMessage);
        foreach (IPAddress targetAddress in broadcastTargets)
        {
            try
            {
                using UdpClient client = new();
                client.EnableBroadcast = true;
                client.Client.ReceiveTimeout = DiscoveryTimeoutMs;
                client.Client.SendTimeout = 1000;
                client.Send(payload, payload.Length, new IPEndPoint(targetAddress, discoveryPort));

                IPEndPoint remoteEndPoint = new(IPAddress.Any, 0);
                byte[] responseBytes = client.Receive(ref remoteEndPoint);
                string response = Encoding.UTF8.GetString(responseBytes).Trim();
                if (!response.StartsWith(DiscoveryResponsePrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                string port = response.Substring(DiscoveryResponsePrefix.Length).Trim();
                return "http://" + remoteEndPoint.Address + ":" + port;
            }
            catch
            {
            }
        }

        return string.Empty;
    }

    private async Task<string> DiscoverBackendViaHttpScanAsync(NetworkDiscoveryContext networkContext)
    {
        List<string> candidateHosts = BuildSubnetCandidates(networkContext);
        if (candidateHosts.Count == 0)
        {
            return string.Empty;
        }

        LogInfo("Iniciando escaneo HTTP en subred. candidatos=" + candidateHosts.Count + ".");

        using CancellationTokenSource cancellationSource = new();
        using HttpClient client = new() { Timeout = TimeSpan.FromMilliseconds(SubnetProbeTimeoutMs) };

        for (int index = 0; index < candidateHosts.Count; index += SubnetProbeBatchSize)
        {
            List<Task<string>> batch = new();
            int batchCount = Math.Min(SubnetProbeBatchSize, candidateHosts.Count - index);
            for (int offset = 0; offset < batchCount; offset++)
            {
                batch.Add(ProbeBackendHttpAsync(client, candidateHosts[index + offset], cancellationSource.Token));
            }

            while (batch.Count > 0)
            {
                Task<string> completedTask = await Task.WhenAny(batch);
                batch.Remove(completedTask);
                string discoveredUrl = await completedTask;
                if (string.IsNullOrWhiteSpace(discoveredUrl))
                {
                    continue;
                }

                cancellationSource.Cancel();
                return discoveredUrl;
            }
        }

        return string.Empty;
    }

    private async Task<string> ProbeBackendHttpAsync(HttpClient client, string host, CancellationToken cancellationToken)
    {
        string candidateBaseUrl = "http://" + host + ":" + BackendHttpPort;
        string candidateUrl = candidateBaseUrl + "/api/device/readings";

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, candidateUrl);
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return string.Empty;
            }

            return candidateBaseUrl;
        }
        catch
        {
            return string.Empty;
        }
    }

    private async Task<string> FetchDashboardJsonAsync(string url)
    {
        using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(3) };
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<string> PostFanOverrideAsync(string url, string payloadJson)
    {
        using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(3) };
        using StringContent content = new(payloadJson, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PostAsync(url, content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private List<string> BuildSubnetCandidates(NetworkDiscoveryContext networkContext)
    {
        List<string> candidates = new();

        if (!string.IsNullOrWhiteSpace(backendBaseUrl) && Uri.TryCreate(backendBaseUrl, UriKind.Absolute, out Uri existingBackendUri))
        {
            candidates.Add(existingBackendUri.Host);
        }

        if (!string.IsNullOrWhiteSpace(networkContext.GatewayIp))
        {
            candidates.Add(networkContext.GatewayIp);
        }

        if (!TryGetIpv4Octets(networkContext.LocalIp, out byte[] localIpOctets))
        {
            return DeduplicateCandidates(candidates, networkContext.LocalIp);
        }

        string subnetPrefix = localIpOctets[0] + "." + localIpOctets[1] + "." + localIpOctets[2] + ".";
        for (int host = 1; host <= 254; host++)
        {
            string candidate = subnetPrefix + host;
            if (candidate == networkContext.LocalIp)
            {
                continue;
            }

            candidates.Add(candidate);
        }

        return DeduplicateCandidates(candidates, networkContext.LocalIp);
    }

    private List<string> DeduplicateCandidates(List<string> candidates, string localIp)
    {
        List<string> deduplicated = new();
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < candidates.Count; index++)
        {
            string candidate = candidates[index];
            if (string.IsNullOrWhiteSpace(candidate) || candidate == localIp || !seen.Add(candidate))
            {
                continue;
            }

            deduplicated.Add(candidate);
        }

        return deduplicated;
    }

    private NetworkDiscoveryContext GetNetworkDiscoveryContext()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using AndroidJavaClass unityPlayer = new("com.unity3d.player.UnityPlayer");
            using AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using AndroidJavaObject applicationContext = activity.Call<AndroidJavaObject>("getApplicationContext");
            using AndroidJavaObject wifiManager = applicationContext.Call<AndroidJavaObject>("getSystemService", "wifi");
            if (wifiManager == null)
            {
                return default;
            }

            using AndroidJavaObject connectionInfo = wifiManager.Call<AndroidJavaObject>("getConnectionInfo");
            using AndroidJavaObject dhcpInfo = wifiManager.Call<AndroidJavaObject>("getDhcpInfo");
            int localIp = connectionInfo != null ? connectionInfo.Call<int>("getIpAddress") : 0;
            int gateway = dhcpInfo != null ? dhcpInfo.Get<int>("gateway") : 0;
            int netmask = dhcpInfo != null ? dhcpInfo.Get<int>("netmask") : 0;
            int broadcast = netmask == 0 ? 0 : (localIp & netmask) | ~netmask;

            return new NetworkDiscoveryContext(
                IntToIpString(localIp),
                IntToIpString(gateway),
                IntToIpString(broadcast),
                CountMaskBits(netmask)
            );
        }
        catch (Exception exception)
        {
            LogWarning("No se pudo leer contexto de red Android: " + exception.Message);
            return default;
        }
#else
        return default;
#endif
    }

    private static bool TryGetIpv4Octets(string ipAddress, out byte[] octets)
    {
        octets = null;
        if (!IPAddress.TryParse(ipAddress, out IPAddress parsedAddress))
        {
            return false;
        }

        byte[] bytes = parsedAddress.GetAddressBytes();
        if (bytes.Length != 4)
        {
            return false;
        }

        octets = bytes;
        return true;
    }

    private static string IntToIpString(int address)
    {
        if (address == 0)
        {
            return string.Empty;
        }

        byte[] bytes =
        {
            (byte)(address & 0xFF),
            (byte)((address >> 8) & 0xFF),
            (byte)((address >> 16) & 0xFF),
            (byte)((address >> 24) & 0xFF),
        };
        return new IPAddress(bytes).ToString();
    }

    private static int CountMaskBits(int netmask)
    {
        int count = 0;
        uint mask = unchecked((uint)netmask);
        while (mask != 0)
        {
            count += (int)(mask & 1);
            mask >>= 1;
        }

        return count;
    }

    private DashboardPayload[] ParseDashboardPayloads(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
        {
            return Array.Empty<DashboardPayload>();
        }

        try
        {
            var wrapped = JsonUtility.FromJson<DashboardPayloadCollection>("{\"items\":" + json + "}");
            if (wrapped == null || wrapped.items == null)
            {
                LogWarning("JsonUtility no pudo materializar lecturas. jsonLength=" + json.Length + ".");
                return Array.Empty<DashboardPayload>();
            }

            return wrapped.items;
        }
        catch (Exception exception)
        {
            LogWarning("Error parseando lecturas: " + exception.Message + ". snippet='" + ClipForLog(json) + "'.");
            return Array.Empty<DashboardPayload>();
        }
    }

    private DashboardPayload ResolveActivePayload(DashboardPayload[] payloads)
    {
        if (payloads == null || payloads.Length == 0)
        {
            return null;
        }

        for (int i = 0; i < payloads.Length; i++)
        {
            if (string.Equals(payloads[i].deviceCode, deviceCode, StringComparison.OrdinalIgnoreCase))
            {
                return payloads[i];
            }
        }

        LogWarning("No se encontro deviceCode='" + deviceCode + "' en la respuesta. Se usara el primero: '" + payloads[0].deviceCode + "'.");
        return payloads[0];
    }

    private static string ClipForLog(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= 220 ? value : value.Substring(0, 220) + "...";
    }

    private static void LogInfo(string message)
    {
        WritePlatformLog("i", message);
        Debug.Log(LogPrefix + message);
    }

    private static void LogWarning(string message)
    {
        WritePlatformLog("w", message);
        Debug.LogWarning(LogPrefix + message);
    }

    private static void WritePlatformLog(string level, string message)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using AndroidJavaClass logClass = new("android.util.Log");
            if (level == "w")
            {
                logClass.CallStatic<int>("w", LogTag, message);
                return;
            }

            logClass.CallStatic<int>("i", LogTag, message);
        }
        catch
        {
        }
#endif
    }

    private string BuildLiveStatusLine(DashboardPayload payload)
    {
        string baseStatus = payload.fanOverrideEnabled
            ? "VENTILADOR MANUAL ACTIVO"
            : payload.relayOn ? "VENTILADOR AUTO ACTIVO" : "TELEMETRIA EN VIVO";
        if (payload.fireAlert)
        {
            return "ALERTA DE FUEGO // " + baseStatus;
        }

        if (payload.gasAlert)
        {
            return "ALERTA DE GAS // " + baseStatus;
        }

        return baseStatus;
    }

    private string BuildModeLabel(DashboardPayload payload)
    {
        if (payload.fanOverrideEnabled)
        {
            return "VENTILACION MANUAL";
        }

        if (payload.fireAlert && payload.relayOn)
        {
            return "AUTO POR FUEGO";
        }

        if (payload.gasAlert && payload.relayOn)
        {
            return "AUTO POR GAS";
        }

        if (payload.fireAlert)
        {
            return "FUEGO DETECTADO";
        }

        if (payload.gasAlert)
        {
            return "GAS DETECTADO";
        }

        return "MONITOREO";
    }

    private string BuildFanLabel(DashboardPayload payload)
    {
        if (payload.fanOverrideEnabled)
        {
            return "MANUAL";
        }

        if (payload.fireAlert && payload.relayOn)
        {
            return "AUTO FUEGO";
        }

        if (payload.gasAlert && payload.relayOn)
        {
            return "AUTO GAS";
        }

        return payload.relayOn ? "ENCENDIDO" : "APAGADO";
    }

    private string BuildBackendLabel()
    {
        string backendLabel = string.IsNullOrWhiteSpace(lastBackendLabel) ? "SIN BACKEND" : lastBackendLabel;
        return backendLabel + " // " + knownDeviceCount + " DISP";
    }

    private Transform ResolveHudAnchor()
    {
        var xrOrigin = FindAnyObjectByType<XROrigin>();
        if (xrOrigin != null && xrOrigin.Camera != null)
        {
            return xrOrigin.Camera.transform;
        }

        if (Camera.main != null)
        {
            return Camera.main.transform;
        }

        var anyCamera = FindAnyObjectByType<Camera>();
        return anyCamera != null ? anyCamera.transform : null;
    }

    private TextMeshProUGUI CreateMetric(RectTransform parent, string label, Vector2 anchoredPosition)
    {
        var panel = CreatePanel(label + "Panel", parent, new Vector2(220f, 130f), new Color(0.04f, 0.12f, 0.18f, 0.75f), new Vector2(0.5f, 0.5f), anchoredPosition);
        CreateText(panel, label, 19, FontStyles.Bold, HudGlow, TextAlignmentOptions.Center, new Vector2(0f, 34f), new Vector2(180f, 28f));
        return CreateText(panel, "--", 34, FontStyles.Bold, Color.white, TextAlignmentOptions.Center, new Vector2(0f, -8f), new Vector2(180f, 42f));
    }

    private TextMeshProUGUI CreateLabelValue(RectTransform parent, string label, Vector2 anchoredPosition)
    {
        var panel = CreatePanel(label + "Panel", parent, new Vector2(360f, 70f), new Color(0.04f, 0.12f, 0.18f, 0.75f), new Vector2(0.5f, 0.5f), anchoredPosition);
        CreateText(panel, label, 17, FontStyles.Bold, HudGlow, TextAlignmentOptions.Left, new Vector2(-132f, 0f), new Vector2(120f, 26f));
        return CreateText(panel, "--", 20, FontStyles.Bold, Color.white, TextAlignmentOptions.Right, new Vector2(42f, 0f), new Vector2(220f, 30f));
    }

    private RectTransform CreatePanel(string name, Transform parent, Vector2 size, Color color, Vector2 anchor, Vector2 anchoredPosition)
    {
        var panelObject = new GameObject(name, typeof(RectTransform), typeof(Image));
        panelObject.transform.SetParent(parent, false);
        var rect = panelObject.GetComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPosition;
        var image = panelObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return rect;
    }

    private void CreateLine(Transform parent, Vector2 anchoredPosition, Vector2 size, Color color)
    {
        CreatePanel("Line", parent, size, color, new Vector2(0.5f, 0.5f), anchoredPosition);
    }

    private TextMeshProUGUI CreateText(Transform parent, string content, float size, FontStyles style, Color color, TextAlignmentOptions alignment, Vector2 anchoredPosition, Vector2 boxSize)
    {
        var textObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        var rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = boxSize;

        var text = textObject.GetComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.text = content;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.enableWordWrapping = false;
        text.raycastTarget = false;
        return text;
    }

    private struct TelemetrySnapshot
    {
        public float TemperatureC;
        public float Humidity;
        public float PressureHpa;
        public float GasLevel;
        public bool WifiConnected;
        public string WifiLabel;
        public string ModeLabel;
        public string FanLabel;
        public bool FanEnabled;
        public string StatusLine;
        public string BackendLabel;
    }

    private struct HandTrackingState
    {
        public bool IsLeftHand;
        public bool IsPinching;
        public Pose WristPose;
        public Pose PalmPose;
        public Pose IndexTipPose;
        public Pose ThumbTipPose;
        public Vector3 PinchPosition;
    }

    [System.Serializable]
    private class DashboardPayloadCollection
    {
        public DashboardPayload[] items;
    }

    private readonly struct DiscoveryAttemptResult
    {
        public DiscoveryAttemptResult(string backendBaseUrl, string method)
        {
            BackendBaseUrl = backendBaseUrl;
            Method = method;
        }

        public string BackendBaseUrl { get; }

        public string Method { get; }
    }

    private readonly struct NetworkDiscoveryContext
    {
        public NetworkDiscoveryContext(string localIp, string gatewayIp, string broadcastIp, int prefixLength)
        {
            LocalIp = localIp;
            GatewayIp = gatewayIp;
            BroadcastIp = broadcastIp;
            PrefixLength = prefixLength;
        }

        public string LocalIp { get; }

        public string GatewayIp { get; }

        public string BroadcastIp { get; }

        public int PrefixLength { get; }

        public string ToSummary()
        {
            return "localIp='" + LocalIp + "' gateway='" + GatewayIp + "' broadcast='" + BroadcastIp + "' prefix=" + PrefixLength;
        }
    }

    [System.Serializable]
    private class DashboardPayload
    {
        public string deviceCode;
        public string deviceName;
        public float temperatureC;
        public float humidity;
        public float pressureHpa;
        public float gasLevel;
        public bool gasAlert;
        public bool fireAlert;
        public bool relayOn;
        public bool fanOverrideEnabled;
        public bool wifiConnected;
        public string status;
    }

    [System.Serializable]
    private class FanOverridePayload
    {
        public string deviceCode;
        public bool enabled;
    }

    [System.Serializable]
    private class FanOverrideResponsePayload
    {
        public string deviceCode;
        public bool enabled;
        public string message;
    }
}