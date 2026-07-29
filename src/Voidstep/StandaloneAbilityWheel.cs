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

        internal void Cleanup()
        {
            Close(false);
            _view.Hide();
        }

        private void Open()
        {
            if (VoidstepInputBindings.Abilities.Length == 0)
            {
                _logger.Info("Standalone ability wheel could not open because no abilities are registered.");
                return;
            }

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
            var count = VoidstepInputBindings.Abilities.Length;
            if (count <= 0)
                return;

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
            var sector = Math.PI * 2.0 / count;
            var index = (int)Math.Floor((angle + sector * 0.5) / sector) % count;
            if (index == _selectedIndex)
                return;
            if (_viewModel != null && !_viewModel.SetSelected(index))
            {
                _logger.Debug("Standalone wheel rejected out-of-range selection index=" + index + ".");
                return;
            }
            _selectedIndex = index;
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
        private bool _layerAdded;

        internal StandaloneAbilityWheelView(VoidstepLogger logger)
        {
            _logger = logger;
        }

        internal bool Show(VoidstepAbilityWheelVM viewModel)
        {
            if (!Hide())
            {
                _logger.Info("Standalone ability wheel retained an earlier Gauntlet layer for cleanup retry; a second layer was not created.");
                return false;
            }

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

                // This layer renders the radial overlay only. Voidstep reads Q and pointer position
                // through its mission input router, so the Gauntlet layer must never take focus or
                // install input restrictions. Doing so captures mouse-wheel input through a full
                // input mask and can disable native weapon cycling for the rest of the mission.
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
                _layerAdded = true;

                _logger.Debug("Standalone ability-wheel overlay attached as a display-only Gauntlet layer; native input remains unowned.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Debug("Standalone Gauntlet ability wheel unavailable; input-only fallback remains active: " + Unwrap(ex).Message);
                Hide();
                return false;
            }
        }

        internal bool Hide()
        {
            if (_layer == null)
            {
                _movie = null;
                _screen = null;
                _layerAdded = false;
                return true;
            }

            if (_movie != null)
            {
                try
                {
                    var release = _layer.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                        .FirstOrDefault(method => method.Name == "ReleaseMovie" && method.GetParameters().Length == 1 &&
                                                  method.GetParameters()[0].ParameterType.IsInstanceOfType(_movie));
                    release?.Invoke(_layer, new[] { _movie });
                    _movie = null;
                }
                catch (Exception ex)
                {
                    _logger.Debug("Standalone wheel movie release failed safely: " + Unwrap(ex).Message);
                }
            }

            if (_layerAdded)
            {
                try
                {
                    if (_screen == null)
                        throw new InvalidOperationException("The owning screen reference is unavailable.");
                    var remove = _screen.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(method => method.Name == "RemoveLayer" && method.GetParameters().Length == 1 &&
                                                  method.GetParameters()[0].ParameterType.IsInstanceOfType(_layer));
                    if (remove == null)
                        throw new MissingMethodException("ScreenBase.RemoveLayer was not found.");
                    remove.Invoke(_screen, new[] { _layer });
                    _layerAdded = false;
                }
                catch (Exception ex)
                {
                    _logger.Info("Standalone ability-wheel layer removal failed; ownership was retained for a later cleanup retry. " + Unwrap(ex).Message);
                    return false;
                }
            }

            _movie = null;
            _layer = null;
            _screen = null;
            return true;
        }

        private static object CreateLayer(Type layerType)
        {
            var signatures = new[]
            {
                new[] { typeof(int) },
                new[] { typeof(int), typeof(string) },
                new[] { typeof(int), typeof(string), typeof(bool) },
                new[] { typeof(string), typeof(int), typeof(bool) }
            };
            var arguments = new[]
            {
                new object[] { LayerOrder },
                new object[] { LayerOrder, "VoidstepAbilityWheel" },
                new object[] { LayerOrder, "VoidstepAbilityWheel", false },
                new object[] { "VoidstepAbilityWheel", LayerOrder, false }
            };

            for (var i = 0; i < signatures.Length; i++)
            {
                var constructor = layerType.GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    signatures[i],
                    null);
                if (constructor == null)
                    continue;
                try { return constructor.Invoke(arguments[i]); }
                catch { }
            }
            return null;
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
