using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using EIOP.Tools;
using GorillaLocomotion;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace EIOP.Core;

public class Notifications : MonoBehaviour
{
    private const int MaxNotifications = 5;
    private const float NotificationDuration = 10f;
    private const float FadeInDuration = 0.3f;
    private const float FadeOutDuration = 0.2f;
    private const int TextWrapWidth = 40;
    
    private const int BaseFontSize = 32;
    private const int MinFontSize = 16;
    private const int FontSizeStep = 4;

    private static readonly Vector3 VRPosition = new(0.1f, 0.2f, 0.6f);
    private static readonly Vector3 PCPosition = new(-0.6793f, 0.5705f, 0.6f);
    private static readonly Quaternion VRRotation = Quaternion.Euler(345f, 0f, 0f);

    private static Notifications instance;

    private readonly Dictionary<Guid, NotificationData> notifications = new();
    private readonly Queue<Guid> notificationOrder = new();

    private GameObject canvas;
    private Text notificationText;
    private CanvasGroup canvasGroup;
    private Coroutine fadeCoroutine;

    private class NotificationData
    {
        public string Message { get; set; }
        public float Timestamp { get; set; }
        public Coroutine RemovalCoroutine { get; set; }
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
    }

    private void Start()
    {
        InitializeCanvas();
    }

    private void InitializeCanvas()
    {
        GameObject canvasPrefab = Plugin.EIOPBundle.LoadAsset<GameObject>("EIOPNotifications");
        Transform parent = GetNotificationParent();
        
        canvas = Instantiate(canvasPrefab, parent);
        Destroy(canvasPrefab);
        
        canvas.name = "EIOP Notifications";
        SetupCanvasTransform();
        SetupCanvasComponents();
    }

    private Transform GetNotificationParent()
    {
        return XRSettings.isDeviceActive 
            ? GTPlayer.Instance.headCollider.transform 
            : PCHandler.ThirdPersonCameraTransform;
    }

    private void SetupCanvasTransform()
    {
        bool isVR = XRSettings.isDeviceActive;
        
        canvas.transform.localPosition = isVR ? VRPosition : PCPosition;
        canvas.transform.localRotation = isVR ? VRRotation : Quaternion.identity;
        canvas.SetLayer(isVR ? UnityLayer.FirstPersonOnly : UnityLayer.MirrorOnly);
    }

    private void SetupCanvasComponents()
    {
        notificationText = canvas.GetComponentInChildren<Text>();
        
        if (notificationText != null)
        {
            notificationText.supportRichText = true;
            notificationText.text = string.Empty;
        }

        canvasGroup = canvas.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = canvas.AddComponent<CanvasGroup>();

        canvasGroup.alpha = 0f;
    }

    public static void SendNotification(string message)
    {
        if (instance == null)
        {
            Debug.LogWarning("[Notifications] Instance not initialized");
            return;
        }

        instance.AddNotification(message);
    }

    private void AddNotification(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        Guid notificationId = Guid.NewGuid();
        string formattedMessage = message.InsertNewlinesWithRichText(TextWrapWidth);

        var notificationData = new NotificationData
        {
            Message = formattedMessage,
            Timestamp = Time.time
        };

        notifications[notificationId] = notificationData;
        notificationOrder.Enqueue(notificationId);

        RemoveOldestIfExceeded();

        notificationData.RemovalCoroutine = StartCoroutine(RemoveNotificationAfterTime(notificationId));

        UpdateNotificationDisplay();
    }

    private void RemoveOldestIfExceeded()
    {
        while (notificationOrder.Count > MaxNotifications)
        {
            Guid oldestId = notificationOrder.Dequeue();
            RemoveNotification(oldestId);
        }
    }

    private void RemoveNotification(Guid notificationId)
    {
        if (!notifications.TryGetValue(notificationId, out var data))
            return;

        if (data.RemovalCoroutine != null)
            StopCoroutine(data.RemovalCoroutine);

        notifications.Remove(notificationId);
    }

    private IEnumerator RemoveNotificationAfterTime(Guid notificationId)
    {
        yield return new WaitForSeconds(NotificationDuration);

        RemoveNotification(notificationId);
        UpdateNotificationDisplay();
    }

    private void UpdateNotificationDisplay()
    {
        if (notificationText == null)
            return;

        string displayText = BuildDisplayText();
        notificationText.text = displayText;

        if (fadeCoroutine != null)
            StopCoroutine(fadeCoroutine);

        fadeCoroutine = StartCoroutine(notifications.Count > 0 
            ? FadeCanvasIn() 
            : FadeCanvasOut());
    }

    private string BuildDisplayText()
    {
        if (notifications.Count == 0)
            return string.Empty;

        var orderedNotifications = notificationOrder
            .Where(id => notifications.ContainsKey(id))
            .Select(id => notifications[id])
            .Reverse()
            .ToList();

        var textBuilder = new System.Text.StringBuilder();

        for (int i = 0; i < orderedNotifications.Count; i++)
        {
            int fontSize = CalculateFontSize(i);
            float alpha = CalculateAlpha(i, orderedNotifications.Count);
            
            string colorHex = ColorToHex(new Color(1f, 1f, 1f, alpha));
            string message = orderedNotifications[i].Message;
            
            textBuilder.Append($"<color={colorHex}><size={fontSize}>{message}</size></color>");
            
            if (i < orderedNotifications.Count - 1)
                textBuilder.Append("\n\n");
        }

        return textBuilder.ToString();
    }

    private int CalculateFontSize(int index)
    {
        int size = BaseFontSize - (FontSizeStep * index);
        return Mathf.Max(MinFontSize, size);
    }

    private float CalculateAlpha(int index, int totalCount)
    {
        float baseAlpha = 1f - (0.15f * index);
        return Mathf.Clamp01(baseAlpha);
    }

    private string ColorToHex(Color color)
    {
        int r = Mathf.RoundToInt(color.r * 255f);
        int g = Mathf.RoundToInt(color.g * 255f);
        int b = Mathf.RoundToInt(color.b * 255f);
        int a = Mathf.RoundToInt(color.a * 255f);
        
        return $"#{r:X2}{g:X2}{b:X2}{a:X2}";
    }

    private IEnumerator FadeCanvasIn()
    {
        float elapsed = 0f;
        float startAlpha = canvasGroup.alpha;

        while (elapsed < FadeInDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / FadeInDuration;
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 1f, EaseOutCubic(t));
            yield return null;
        }

        canvasGroup.alpha = 1f;
        fadeCoroutine = null;
    }

    private IEnumerator FadeCanvasOut()
    {
        float elapsed = 0f;
        float startAlpha = canvasGroup.alpha;

        while (elapsed < FadeOutDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / FadeOutDuration;
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, EaseInCubic(t));
            yield return null;
        }

        canvasGroup.alpha = 0f;
        fadeCoroutine = null;
    }

    private float EaseOutCubic(float t)
    {
        return 1f - Mathf.Pow(1f - t, 3f);
    }

    private float EaseInCubic(float t)
    {
        return t * t * t;
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }
}
