# The HD pack

Pictures for OpenGG's own look (the game without a skin): surfaces for the JunkYard, the Car Lot and the Auction, and
the bare metal of damaged parts. This repository did not make them, so they are not `ai_` files.

All are **CC0 1.0 Universal** (public domain): free to use, change and share for any purpose, without asking or
crediting. The sources are listed anyway.

| Folder | Source | Licence | Used for |
|---|---|---|---|
| `textures/Asphalt031` | [ambientCG: Asphalt 031](https://ambientcg.com/view?id=Asphalt031) | [CC0 1.0](https://docs.ambientcg.com/license/) | the Car Lot (graded darker) |
| `textures/Ground109` | [ambientCG: Ground 109](https://ambientcg.com/view?id=Ground109) | [CC0 1.0](https://docs.ambientcg.com/license/) | the JunkYard's ground |
| `textures/Grass004` | [ambientCG: Grass 004](https://ambientcg.com/view?id=Grass004) | [CC0 1.0](https://docs.ambientcg.com/license/) | the Auction's lawn, the field past the Car Lot |
| `textures/Planks037A` | [ambientCG: Planks 037 A](https://ambientcg.com/view?id=Planks037A) | [CC0 1.0](https://docs.ambientcg.com/license/) | the Auction's stage and chairs, the JunkYard's shelf (graded to the placeholders' wood) |
| `textures/Metal041B` | [ambientCG: Metal 041 B](https://ambientcg.com/view?id=Metal041B) | [CC0 1.0](https://docs.ambientcg.com/license/) | damaged parts' bare metal: its streaks under the condition's colour |
| `textures/Fence003` | [ambientCG: Fence 003](https://ambientcg.com/view?id=Fence003) | [CC0 1.0](https://docs.ambientcg.com/license/) | the JunkYard's chain-link fence |

From each 1K-JPG download we kept the colour, the normal map (OpenGL convention, `NormalGL`) and the roughness (for
the fence the colour and the opacity), re-saved as JPEG at 1024 × 1024. The game grades some colours to the
placeholders' palette as it loads them (`ModernLook.Surface`) and widens the fence's wires.
