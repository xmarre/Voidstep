using System;
using System.Linq;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using Voidstep.Core;

namespace Voidstep
{
    internal sealed class StandaloneAbilityWheel
    {
        private const float MinimumSelectionRadius = 36f;
        private readonly VoidstepLogger _logger;
        private readonly Action<AbilityId> _onSelected;
        private readonly StandaloneAbilityWheelView _view;
        private VoidstepAbilityWheelVM _viewModel;
        private bool _open;
        private int _selectedIndex;

        internal StandaloneAbilityWheel(VoidstepLogger logger, Action<AbilityId> onSelected)
        {
            _logger = logger;
            _onSelected = onSelected;
            _view = new StandaloneAbilityWheelView(logger);
        }

        internal bool IsOpen => _open;

        internal void Tick()
        {
            bool pressed;
            bool down;
            bool released;
            bool escape;
            using (InputConflictSuppression.EnterBypass())
            {
                pressed = Input.IsKeyPressed(InputKey.Q);
                down = Input.IsKeyDown(InputKey.Q) || Input.IsKeyDownImmediate(InputKey.Q);
                released = Input.IsKeyReleased(InputKey.Q);
                escape = Input.IsKeyPressed(InputKey.Escape);
            }

            if (!_open && pressed)
                Open();
            if (!_open)
                return;

            UpdateSelection();
            if (escape)
            {
                Close(false);
                return;
            }
            if (released || (!down && !pressed))
                Close(true);
        }

        internal void Cleanup() => Close(false);

        private void Open()
        {
            _open = true;
            _selectedIndex = 0;
            _viewModel = new VoidstepAbilityWheelVM();
            try
            {
                Input.SetMousePosition(
                    (int)(Screen.RealScreenResolutionWidth * 0.5f),
                    (int)(Screen.RealScreenResolutionHeight * 0.5f));
            }
            catch (Exception ex)
            {
                _logger.Debug("Standalone wheel could not centre the mouse: " + ex.Message);
            }

            if (!_view.Show(_viewModel))
                TryDisplayNotice("Voidstep ability wheel active. Move the mouse around centre and release Q to select.");
            _logger.Debug("Standalone Voidstep Q wheel opened.");
        }

        private void UpdateSelection()
        {
            var centre = new Vec2(
                Screen.RealScreenResolutionWidth * 0.5f,
                Screen.RealScreenResolutionHeight * 0.5f);
            Vec2 mouse;
            using (InputConflictSuppression.EnterBypass())
                mouse = Input.MousePositionPixel;
            var delta = mouse - centre;
            if (delta.Length < MinimumSelectionRadius)
                return;

            var angle = Math.Atan2(delta.x, -delta.y);
            if (angle < 0.0)
                angle += Math.PI * 2.0;
            var index = (int)Math.Floor((angle + Math.PI / 6.0) / (Math.PI / 3.0)) % 6;
            if (index == _selectedIndex)
                return;
            _selectedIndex = index;
            _viewModel?.SetSelected(index);
        }

        private void Close(bool select)
        {
            if (!_open)
                return;
            _open = false;
            _view.Hide();
            var selected = _selectedIndex;
            _viewModel = null;
            _logger.Debug(select
                ? "Standalone Voidstep Q wheel selected index=" + selected + "."
                : "Standalone Voidstep Q wheel cancelled.");
            if (select && selected >= 0 && selected < VoidstepInputBindings.Abilities.Length)
                _onSelected?.Invoke(VoidstepInputBindings.Abilities[selected]);
        }

        private static void TryDisplayNotice(string message)
        {
            try { InformationManager.DisplayMessage(new InformationMessage(message)); }
            catch { }
        }
    }

    internal sealed class StandaloneAbilityWheelView
    {
        private const int LayerOrder = 4500;
        private readonly VoidstepLogger _logger;
        private object _layer;
        private object _movie;
        private object _screen;

        internal StandaloneAbilityWheelView(VoidstepLogger logger)
        {
            _logger = logger;
        }

        internal bool Show(VoidstepAbilityWheelVM viewModel)
        {
            Hide();
            try
            {
                var gauntletLayerType = FindType("TaleWorlds.Engine.GauntletUI.GauntletLayer");
                var screenManagerType = FindType("TaleWorlds.ScreenSystem.ScreenManager");
                if (gauntletLayerType == null || screenManagerType == null)
                    throw new InvalidOperationException("GauntletLayer or ScreenManager is not loaded.");

                _screen = screenManagerType.GetProperty("TopScreen", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
                if (_screen == null)
                    throw new InvalidOperationException("No active Bannerlord screen is available.");

                _layer = CreateLayer(gauntletLayerType);
                if (_layer == null)
                    throw new MissingMethodException("No compatible GauntletLayer constructor was found.");

                SetPropertyIfPresent(_layer, "IsFocusLayer", true);
                ConfigureInputRestrictions(_layer);

                var loadMovie = gauntletLayerType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .FirstOrDefault(method =>
                    {
                        if (method.Name != "LoadMovie") return false;
                        var parameters = method.GetParameters();
                        return parameters.Length == 2 && parameters[0].ParameterType == typeof(string) &&
                               parameters[1].ParameterType.IsAssignableFrom(viewModel.GetType());
                    }) ?? gauntletLayerType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .FirstOrDefault(method => method.Name == "LoadMovie" && method.GetParameters().Length == 2);
                if (loadMovie == null)
                    throw new MissingMethodException("GauntletLayer.LoadMovie was not found.");
                _movie = loadMovie.Invoke(_layer, new object[] { "VoidstepAbilityWheel", viewModel });

                var addLayer = _screen.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(method => method.Name == "AddLayer" && method.GetParameters().Length == 1 &&
                                              method.GetParameters()[0].ParameterType.IsInstanceOfType(_layer));
                if (addLayer == null)
                    throw new MissingMethodException("ScreenBase.AddLayer was not found.");
                addLayer.Invoke(_screen, new[] { _layer });

                InvokeStaticLayerMethod(screenManagerType, "TrySetFocus", _layer);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Debug("Standalone Gauntlet ability wheel unavailable; input-only fallback remains active: " + Unwrap(ex).Message);
                Hide();
                return false;
            }
        }

        internal void Hide()
        {
            if (_layer == null)
            {
                _movie = null;
                _screen = null;
                return;
            }

            try
            {
                var screenManagerType = FindType("TaleWorlds.ScreenSystem.ScreenManager");
                if (screenManagerType != null)
                    InvokeStaticLayerMethod(screenManagerType, "TryLoseFocus", _layer);
            }
            catch { }

            try
            {
                if (_movie != null)
                {
                    var release = _layer.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                        .FirstOrDefault(method => method.Name == "ReleaseMovie" && method.GetParameters().Length == 1 &&
                                                  method.GetParameters()[0].ParameterType.IsInstanceOfType(_movie));
                    release?.Invoke(_layer, new[] { _movie });
                }
            }
            catch { }

            try
            {
                if (_screen != null)
                {
                    var remove = _screen.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(method => method.Name == "RemoveLayer" && method.GetParameters().Length == 1 &&
                                                  method.GetParameters()[0].ParameterType.IsInstanceOfType(_layer));
                    remove?.Invoke(_screen, new[] { _layer });
                }
            }
            catch { }

            _movie = null;
            _layer = null;
            _screen = null;
        }

        private static object CreateLayer(Type layerType)
        {
            foreach (var constructor in layerType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var parameters = constructor.GetParameters();
                var arguments = new object[parameters.Length];
                var supported = true;
                for (var i = 0; i < parameters.Length; i++)
                {
                    var type = parameters[i].ParameterType;
                    if (type == typeof(int)) arguments[i] = LayerOrder;
                    else if (type == typeof(string)) arguments[i] = "VoidstepAbilityWheel";
                    else if (type == typeof(bool)) arguments[i] = true;
                    else { supported = false; break; }
                }
                if (!supported) continue;
                try { return constructor.Invoke(arguments); }
                catch { }
            }
            return null;
        }

        private static void ConfigureInputRestrictions(object layer)
        {
            try
            {
                var restrictions = layer.GetType().GetProperty("InputRestrictions", BindingFlags.Instance | BindingFlags.Public)?.GetValue(layer, null);
                if (restrictions == null) return;
                var method = restrictions.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .FirstOrDefault(candidate => candidate.Name == "SetInputRestrictions" && candidate.GetParameters().Length >= 1);
                if (method == null) return;
                var parameters = method.GetParameters();
                var arguments = new object[parameters.Length];
                for (var i = 0; i < parameters.Length; i++)
                {
                    var type = parameters[i].ParameterType;
                    if (type == typeof(bool)) arguments[i] = true;
                    else if (type.IsEnum) arguments[i] = Enum.ToObject(type, -1);
                    else if (parameters[i].HasDefaultValue) arguments[i] = parameters[i].DefaultValue;
                    else return;
                }
                method.Invoke(restrictions, arguments);
            }
            catch { }
        }

        private static void SetPropertyIfPresent(object instance, string name, object value)
        {
            var property = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanWrite)
                property.SetValue(instance, value, null);
        }

        private static void InvokeStaticLayerMethod(Type type, string name, object layer)
        {
            var method = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(candidate => candidate.Name == name && candidate.GetParameters().Length == 1 &&
                                             candidate.GetParameters()[0].ParameterType.IsInstanceOfType(layer));
            method?.Invoke(null, new[] { layer });
        }

        private static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type;
                try { type = assembly.GetType(fullName, false); }
                catch { continue; }
                if (type != null) return type;
            }
            return null;
        }

        private static Exception Unwrap(Exception exception)
        {
            while (exception is TargetInvocationException invocation && invocation.InnerException != null)
                exception = invocation.InnerException;
            return exception;
        }
    }
}
