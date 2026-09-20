namespace Mme.App.ViewModels;

/// <summary>Beta 32 — Exp/Hr "Paste Party" (frmPasteChar.bPasteParty flow).</summary>
public sealed partial class MainViewModel
{
    /// <summary>Builds the party-paste screen model over the open database
    /// (null without one).</summary>
    public PartyPasteVm? CreatePartyPasteVm()
    {
        if (_db is null) return null;
        double nmr = _nmrVer > 0 ? _nmrVer
            : Core.Text.TextUtils.ExtractNumbersFromString(_db.GetInfoNmrVersion());
        var svc = new Mme.Data.PartyPasteService(_db, Rules, nmr, OnlyInGame);
        _spellUsability ??= new Mme.Data.SpellUsabilityService(_db, GreaterMud,
            disableKaiAutolearn: DisableKaiAutolearn);
        return new PartyPasteVm(this, svc, _spellUsability);
    }

    /// <summary>The Exp/Hr strip fields are plain properties; after a bulk
    /// write (Paste Party Continue) push change notices so the boxes refresh.</summary>
    internal void NotifyExpHrStrip()
    {
        foreach (string p in new[] { nameof(PartySize), nameof(CharDamageThreshold),
            nameof(CharHp), nameof(CharHpRegen), nameof(CharAccuracy), nameof(CharDamage),
            nameof(CharSpellDamage), nameof(PartyAc), nameof(PartyDr), nameof(PartyMr),
            nameof(PartyDodge), nameof(PartyAntiMagicCount) })
            OnChanged(p);
    }
}
