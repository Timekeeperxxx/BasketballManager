using System;
using BasketballManager.UI.Core;
using UnityEngine.UIElements;

namespace BasketballManager.UI.Screens
{
    public sealed class OptionsScreen : UIToolkitScreenBase
    {
        public const string Id = "Options";

        public event Action OnBackClicked;
        public event Action OnRosterClicked;

        private void Awake() { ScreenId = Id; }

        protected override void OnBuilt()
        {
            Root.Q<Button>("btn-back").clicked += () => OnBackClicked?.Invoke();
            Root.Q<Button>("btn-roster").clicked += () => OnRosterClicked?.Invoke();
        }
    }
}
