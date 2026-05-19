using UnityEngine;
using UnityEngine.UIElements;

namespace BasketballManager.UI.Core
{
    /// <summary>
    /// UI Toolkit 屏幕基类。每个屏幕克隆一份 UXML 到 host，
    /// 通过 USS display 控制可见性，并在 Enter/Exit 时机刷新数据。
    /// </summary>
    public abstract class UIToolkitScreenBase : MonoBehaviour
    {
        public string ScreenId { get; protected set; }
        public VisualElement Root { get; private set; }

        /// <summary>
        /// 由 router/host 调用一次，把 UXML 克隆进 host 容器。
        /// </summary>
        public void BuildUi(VisualElement host, VisualTreeAsset uxml, params StyleSheet[] styles)
        {
            if (uxml == null)
            {
                Debug.LogError($"UIToolkitScreenBase: UXML asset is null for screen '{ScreenId ?? GetType().Name}'. Skipping.");
                return;
            }
            Root = uxml.Instantiate();
            Root.style.flexGrow = 1f;
            Root.style.display = DisplayStyle.None;
            if (styles != null)
            {
                foreach (var s in styles)
                {
                    if (s != null) Root.styleSheets.Add(s);
                }
            }
            host.Add(Root);
            OnBuilt();
        }

        public virtual void Show()
        {
            if (Root == null) return;
            Root.style.display = DisplayStyle.Flex;
            OnEnter();
        }

        public virtual void Hide()
        {
            if (Root == null) return;
            OnExit();
            Root.style.display = DisplayStyle.None;
        }

        protected virtual void OnBuilt() { }
        protected virtual void OnEnter() { }
        protected virtual void OnExit() { }
    }
}
