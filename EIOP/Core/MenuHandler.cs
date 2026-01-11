using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EIOP.Tab_Handlers;
using EIOP.Tools;
using TMPro;
using UnityEngine;

namespace EIOP.Core;

public class MenuHandler : MonoBehaviour
{
    private const float AnimationDuration = 0.2f;
    private const float OvershootAmount = 1.1f;
    
    public static readonly Vector3 TargetMenuScale = Vector3.one * 15f;
    public static readonly Vector3 BaseMenuPosition = new(0.25f, 0f, 0.05f);
    public static readonly Quaternion BaseMenuRotation = Quaternion.Euler(300f, 0f, 180f);

    public bool IsMenuOpen { get; private set; }
    public GameObject Menu { get; private set; }

    private bool wasPressed;
    private Coroutine currentAnimation;
    private readonly Dictionary<Transform, TabHandlerBase> tabHandlers = new();

    private void Start()
    {
        InitializeMenu();
        SetUpTabs();
        gameObject.AddComponent<PCHandler>().MenuHandlerInstance = this;
    }

    private void Update()
    {
        bool isPressed = ControllerInputPoller.instance.leftControllerSecondaryButton;

        if (isPressed && !wasPressed)
            ToggleMenu();

        wasPressed = isPressed;
    }

    private void InitializeMenu()
    {
        GameObject menuPrefab = Plugin.EIOPBundle.LoadAsset<GameObject>("Menu");
        Menu = Instantiate(menuPrefab, EIOPUtils.RealLeftController);
        Destroy(menuPrefab);
        
        Menu.name = "EIOP Menu";
        Menu.transform.localPosition = BaseMenuPosition;
        Menu.transform.localRotation = BaseMenuRotation;
        Menu.transform.localScale = Vector3.zero;

        var menuRenderer = Menu.GetComponent<Renderer>();
        if (menuRenderer != null)
            Plugin.MainColour = menuRenderer.material.color;

        var modePanel = Menu.transform.Find("ModePanel");
        if (modePanel != null)
        {
            var modePanelRenderer = modePanel.GetComponent<Renderer>();
            if (modePanelRenderer != null)
                Plugin.SecondaryColour = modePanelRenderer.material.color;
        }

        Menu.SetActive(false);
        PerformShaderManagement(Menu);
    }

    private void ToggleMenu()
    {
        IsMenuOpen = !IsMenuOpen;
        
        if (currentAnimation != null)
            StopCoroutine(currentAnimation);

        currentAnimation = StartCoroutine(IsMenuOpen ? AnimateMenuOpen() : AnimateMenuClose());
    }

    private void SetUpTabs()
    {
        var tabHandlerTypes = GetTabHandlerTypes();
        var tabViews = GetTabViews();
        var tabButtons = GetTabButtons();

        if (tabViews.Count == 0)
        {
            Debug.LogWarning("[MenuHandler] No tab views found in menu");
            return;
        }

        foreach (var tabHandlerType in tabHandlerTypes)
        {
            string tabName = tabHandlerType.Name.Replace("Handler", "");
            
            var tabView = FindTabComponent(tabViews, tabName, "View");
            var tabButton = FindTabComponent(tabButtons, tabName, "Button");

            if (tabView == null || tabButton == null)
            {
                Debug.LogWarning($"[MenuHandler] Missing View or Button for tab: {tabName}");
                continue;
            }

            SetupTabButton(tabButton, tabView, tabViews);
            SetupTabView(tabView, tabHandlerType);
        }

        if (tabViews.Count > 0)
            tabViews[0].gameObject.SetActive(true);
    }

    private List<Type> GetTabHandlerTypes()
    {
        return Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => !t.IsAbstract && 
                       t.IsClass && 
                       typeof(TabHandlerBase).IsAssignableFrom(t))
            .ToList();
    }

    private List<Transform> GetTabViews()
    {
        return Menu.GetComponentsInChildren<Transform>(true)
            .Where(t => t.name.EndsWith("View", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private List<Transform> GetTabButtons()
    {
        var modePanel = Menu.transform.Find("ModePanel");
        if (modePanel == null)
            return new List<Transform>();

        return modePanel.GetComponentsInChildren<Transform>(true)
            .Where(t => t.name.EndsWith("Button", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private Transform FindTabComponent(List<Transform> components, string tabName, string suffix)
    {
        return components.FirstOrDefault(t => 
            t.name.Equals($"{tabName}{suffix}", StringComparison.OrdinalIgnoreCase));
    }

    private void SetupTabButton(Transform button, Transform targetView, List<Transform> allViews)
    {
        var eiopButton = button.gameObject.AddComponent<EIOPButton>();
        eiopButton.OnPress = () => SwitchToTab(targetView, allViews);
    }

    private void SetupTabView(Transform view, Type handlerType)
    {
        var handler = view.gameObject.AddComponent(handlerType) as TabHandlerBase;
        if (handler != null)
            tabHandlers[view] = handler;
        
        view.gameObject.SetActive(false);
    }

    private void SwitchToTab(Transform targetView, List<Transform> allViews)
    {
        foreach (var view in allViews)
            view.gameObject.SetActive(false);

        targetView.gameObject.SetActive(true);
    }

    public static void PerformShaderManagement(GameObject obj)
    {
        ProcessShaders(obj.transform);
    }

    private static void ProcessShaders(Transform transform)
    {
        foreach (Transform child in transform)
            ProcessShaders(child);

        var obj = transform.gameObject;

        if (obj.TryGetComponent<Renderer>(out var renderer))
            UpdateRendererShader(renderer);

        if (obj.TryGetComponent<TextMeshPro>(out var tmp))
            UpdateTextShader(tmp.fontMaterial);

        if (obj.TryGetComponent<TextMeshProUGUI>(out var tmpUGUI))
            UpdateTextShader(tmpUGUI.fontMaterial);
    }

    private static void UpdateRendererShader(Renderer renderer)
    {
        if (!renderer.material.shader.name.Contains("Universal"))
            return;

        renderer.material.shader = Plugin.UberShader;
        
        if (renderer.material.mainTexture != null)
            renderer.material.EnableKeyword("_USE_TEXTURE");
    }

    private static void UpdateTextShader(Material fontMaterial)
    {
        if (fontMaterial == null)
            return;

        var shader = Shader.Find("TextMeshPro/Mobile/Distance Field");
        if (shader != null)
            fontMaterial.shader = shader;
    }

    private IEnumerator AnimateMenuOpen()
    {
        Menu.SetActive(true);
        Menu.transform.localScale = Vector3.zero;
        
        float elapsed = 0f;

        while (elapsed < AnimationDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / AnimationDuration;
            
            float easedT = EaseOutBack(t);
            Menu.transform.localScale = Vector3.Lerp(Vector3.zero, TargetMenuScale, easedT);

            yield return null;
        }

        Menu.transform.localScale = TargetMenuScale;
        currentAnimation = null;
    }

    private IEnumerator AnimateMenuClose()
    {
        Menu.transform.localScale = TargetMenuScale;
        
        float elapsed = 0f;

        while (elapsed < AnimationDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / AnimationDuration;
            
            float easedT = EaseInBack(t);
            Menu.transform.localScale = Vector3.Lerp(TargetMenuScale, Vector3.zero, easedT);

            yield return null;
        }

        Menu.transform.localScale = Vector3.zero;
        Menu.SetActive(false);
        currentAnimation = null;
    }

    private float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }

    private float EaseInBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return c3 * t * t * t - c1 * t * t;
    }
}
