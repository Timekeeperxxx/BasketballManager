using System;
using System.Collections.Generic;

namespace BasketballManager.UI.Core
{
    /// <summary>
    /// 极简栈式路由：Push 进入新页（旧页 Hide），Pop 返回（栈顶 Hide，下一层 Show）。
    /// ReplaceRoot 清栈后压入新根（用于 MainMenu）。
    /// CurrentChanged 触发后由 AppShell 控制 debug FAB 可见性等装饰元素。
    /// </summary>
    public sealed class ScreenRouter
    {
        private readonly Dictionary<string, UIToolkitScreenBase> _screens = new Dictionary<string, UIToolkitScreenBase>();
        private readonly Stack<string> _stack = new Stack<string>();

        public event Action<string> CurrentChanged;

        public string Current => _stack.Count > 0 ? _stack.Peek() : null;

        public int StackDepth => _stack.Count;

        public void Register(UIToolkitScreenBase screen)
        {
            if (screen == null || string.IsNullOrEmpty(screen.ScreenId)) return;
            _screens[screen.ScreenId] = screen;
        }

        public bool IsRegistered(string id) => !string.IsNullOrEmpty(id) && _screens.ContainsKey(id);

        public void Push(string id)
        {
            if (!_screens.TryGetValue(id, out var next)) return;
            if (Current == id) return;
            if (Current != null && _screens.TryGetValue(Current, out var prev)) prev.Hide();
            _stack.Push(id);
            next.Show();
            CurrentChanged?.Invoke(id);
        }

        public void Pop()
        {
            if (_stack.Count <= 1) return;
            var top = _stack.Pop();
            if (_screens.TryGetValue(top, out var leaving)) leaving.Hide();
            if (_screens.TryGetValue(Current, out var below)) below.Show();
            CurrentChanged?.Invoke(Current);
        }

        public void ReplaceRoot(string id)
        {
            if (!_screens.ContainsKey(id)) return;
            while (_stack.Count > 0)
            {
                var top = _stack.Pop();
                if (_screens.TryGetValue(top, out var s)) s.Hide();
            }
            _stack.Push(id);
            _screens[id].Show();
            CurrentChanged?.Invoke(id);
        }
    }
}
