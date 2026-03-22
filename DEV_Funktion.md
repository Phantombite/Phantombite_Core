# DEV Funktion — PhantomBite Core

## Zweck
PhantomBite Core ist der Basis-Mod für alle PhantomBite Mods. Er stellt gemeinsam genutzte Item-Definitionen bereit damit diese nicht in jedem Mod doppelt vorhanden sind.

## Inhalt

### AdminChip
- **TypeId:** Component
- **SubtypeId:** AdminChip
- **Masse:** 0.25 kg
- **Volumen:** 0.2 L
- **Mindestpreis:** 100.000 Credits
- **Blueprint Bauzeit:** 99.999 Sekunden
- **Drop-Wahrscheinlichkeit:** 10%
- **Modell:** Models/Items/AdminChip_Item.mwm
- **Icon:** Textures/GUI/Icons/Items/AdminChip_Item.dds

Der AdminChip ist eine nicht herstellbare, nicht kaufbare Komponente die als Sicherheitsmechanismus dient. Blöcke die den AdminChip als CriticalComponent verwenden können nur von Server-Admins platziert werden da normale Spieler den Chip weder herstellen noch kaufen können.

## Dateistruktur
```
Phantombite_Core/
├── modinfo.sbmi                          (Workshop ID: 3689625814)
├── metadata.mod                          (Version: 1.0)
├── Data/
│   ├── PhysicalItems/AdminChip.sbc       (Item Definition)
│   └── Blueprints/AdminChip.sbc          (Blueprint Definition)
├── Models/Items/AdminChip_Item.mwm       (3D Modell)
└── Textures/GUI/Icons/Items/
    └── AdminChip_Item.dds                (Icon)
```
