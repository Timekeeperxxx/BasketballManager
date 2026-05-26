using System;
using BasketballManager.UI.Core;
using UnityEngine.UIElements;

namespace BasketballManager.UI.Screens
{
    public sealed class MainMenuScreen : UIToolkitScreenBase
    {
        public const string Id = "MainMenu";

        public event Action OnSeasonClicked;
        public event Action OnOptionsClicked;
        public event Action OnQuitClicked;

        private void Awake() { ScreenId = Id; }

        protected override void OnBuilt()
        {
            Root.Q<Button>("btn-season").clicked  += () => OnSeasonClicked?.Invoke();
            Root.Q<Button>("btn-options").clicked += () => OnOptionsClicked?.Invoke();
            Root.Q<Button>("btn-quit").clicked    += () => OnQuitClicked?.Invoke();
        }
    }
}
