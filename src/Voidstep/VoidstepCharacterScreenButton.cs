using System;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.Data;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace Voidstep
{
    internal sealed class VoidstepCharacterScreenButtonVM : ViewModel
    {
        private readonly Action _openMastery;

        internal VoidstepCharacterScreenButtonVM(Action openMastery)
        {
            _openMastery = openMastery;
        }

        [DataSourceProperty]
        public string ButtonText => "Voidstep Mastery";

        public void ExecuteOpenMastery()
        {
            _openMastery?.Invoke();
        }
    }

    internal sealed class VoidstepCharacterScreenButtonController
    {
        private readonly Action _openMastery;
        private ScreenBase _screen;
        private GauntletLayer _layer;
        private GauntletMovieIdentifier _movie;
        private VoidstepCharacterScreenButtonVM _dataSource;
        private bool _suspended;

        internal VoidstepCharacterScreenButtonController(Action openMastery)
        {
            _openMastery = openMastery;
        }

        internal void Tick()
        {
            if (_suspended)
            {
                Detach();
                return;
            }

            var top = ScreenManager.TopScreen;
            if (!IsCharacterDeveloperScreen(top))
            {
                Detach();
                return;
            }

            if (ReferenceEquals(_screen, top) && _layer != null) return;
            Detach();
            Attach(top);
        }

        internal void Suspend()
        {
            _suspended = true;
            Detach();
        }

        internal void Resume()
        {
            _suspended = false;
        }

        internal void Detach()
        {
            try { _layer?.InputRestrictions.ResetInputRestrictions(); }
            catch { }

            try
            {
                if (_layer != null && _movie != null) _layer.ReleaseMovie(_movie);
            }
            catch { }

            try { _dataSource?.OnFinalize(); }
            catch { }

            try
            {
                if (_screen != null && _layer != null) _screen.RemoveLayer(_layer);
            }
            catch { }

            _movie = null;
            _dataSource = null;
            _layer = null;
            _screen = null;
        }

        internal static bool IsCharacterDeveloperScreen(ScreenBase screen)
        {
            if (screen == null) return false;
            var name = screen.GetType().Name;
            return name.IndexOf("CharacterDeveloperScreen", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void Attach(ScreenBase screen)
        {
            try
            {
                _screen = screen;
                _dataSource = new VoidstepCharacterScreenButtonVM(_openMastery);
                _layer = new GauntletLayer("VoidstepCharacterButton", 221);
                _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.MouseButtons);
                _movie = _layer.LoadMovie("VoidstepCharacterButton", _dataSource);
                _screen.AddLayer(_layer);
            }
            catch
            {
                Detach();
            }
        }
    }
}
