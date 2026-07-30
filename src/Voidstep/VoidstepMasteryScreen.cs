using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.Data;
using TaleWorlds.ScreenSystem;

namespace Voidstep
{
    internal sealed class VoidstepMasteryScreen : ScreenBase
    {
        private VoidstepMasteryVM _dataSource;
        private GauntletLayer _layer;
        private GauntletMovieIdentifier _movie;

        protected override void OnInitialize()
        {
            base.OnInitialize();
            _dataSource = new VoidstepMasteryVM(Close);
            _layer = new GauntletLayer("VoidstepMastery", 100) { IsFocusLayer = true };
            AddLayer(_layer);
            _layer.InputRestrictions.SetInputRestrictions();
            _movie = _layer.LoadMovie("VoidstepMastery", _dataSource);
        }

        protected override void OnActivate()
        {
            base.OnActivate();
            ScreenManager.TrySetFocus(_layer);
        }

        protected override void OnDeactivate()
        {
            base.OnDeactivate();
            if (_layer == null) return;
            _layer.IsFocusLayer = false;
            ScreenManager.TryLoseFocus(_layer);
        }

        protected override void OnFinalize()
        {
            if (_layer != null && _movie != null) _layer.ReleaseMovie(_movie);
            _dataSource?.OnFinalize();
            if (_layer != null) RemoveLayer(_layer);
            _movie = null;
            _layer = null;
            _dataSource = null;
            base.OnFinalize();
        }

        private static void Close()
        {
            if (ScreenManager.TopScreen is VoidstepMasteryScreen)
                ScreenManager.PopScreen();
        }
    }
}
