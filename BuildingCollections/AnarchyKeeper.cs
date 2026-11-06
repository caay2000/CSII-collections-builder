namespace BuildingCollections
{
    public sealed class AnarchyKeeper
    {
        bool turnedOnByUs;
        bool warned;

        public void Update(bool wanted)
        {
            if (!AnarchyBridge.Available)
            {
                return;
            }

            if (wanted)
            {
                KeepOn();
            }
            else
            {
                Restore();
            }
        }

        public void Restore()
        {
            if (!turnedOnByUs)
            {
                return;
            }

            turnedOnByUs = false;
            if (AnarchyBridge.Enabled && AnarchyBridge.SetEnabled(false))
            {
                Mod.Log.Info("Anarchy: turned off again (the builder had turned it on).");
            }
        }

        void KeepOn()
        {
            if (AnarchyBridge.Enabled)
            {
                warned = false;
                return;
            }

            if (AnarchyBridge.SetEnabled(true))
            {
                turnedOnByUs = true;
                warned = false;
                Mod.Log.Info("Anarchy: turned on by the builder (buildings may overlap).");
                return;
            }

            if (!warned)
            {
                warned = true;
                Mod.Log.Warn("Anarchy: could not be turned on; overlap will not apply until it is turned on by hand.");
            }
        }
    }
}
