<h1 align="center">
  <img src="icon.png" alt="EmpireCraft Logo" width="150" />
  <br/>
  EmpireCraft
</h1>

# EmpireCraft

---
## Overview

**EmpireCraft** is a large-scale political and historical simulation mod for **WorldBox**.  
It expands the base game with interconnected systems for **empires, legitimacy, titles, imperial cores, factions, bureaucracy, local governance, law, succession, war, and occupation**.

---
## Core Features

### 1. Empire System

- Empire formation, succession, collapse, repair, and reconstruction
- Mandate / legitimacy, central-local relations, imperial history, and emperor turnover
- Support for imperial member kingdoms, tributaries, alliance relations, and layered sovereignty

### 2. Kingdom Titles and Imperial Cores

- Kingdom identity based on main title, capital title, and ancestral title
- Imperial cores that can absorb multiple kingdom titles
- Imperial cores influence imperial rise conditions, unification conflicts, legacy claims, and map presentation
- Dedicated imperial core window, history collection, renaming, and debug tools

### 3. Faction and Court Politics

- Fixed factions, faction leaders, members, supporters, and faction-backed rebellions
- Factions can push claims, build influence, and destabilize imperial politics
- Factions create long-term internal pressure instead of static state behavior

### 4. Bureaucracy and Regional Governance

- Central and local office systems
- Different categories for kingdom, city, army, and court offices
- Regime, culture, and institutional configuration working together
- Includes a standalone regime editor for content authoring

### 5. Claim System

- Claims can be pushed by central authority or local political actors
- Claim execution depends on political status, influence, supporters, factions, and legal conditions
- Includes claims for taxation, religion, reform, official removal, anti-feudatory action, and unification

### 6. Law, Crime, and Corruption

- Official crimes, exposure, punishment, dismissal, and political retaliation
- Corruption is tied to crime probability, governance quality, and stability
- Includes tyranny / brutality style values and escalating legal consequences

### 7. War, Frontline, and Occupation

- Supports both vanilla occupation and custom zone-based occupation
- Frontline-aware soldier movement, zone expansion, zone recovery, and layered siege behavior
- Capture events for lords, kings, and emperors with political consequences

### 8. History and Visualization

- Imperial history, emperor history, imperial core history collections
- Map layers, tooltips, custom nameplates, history windows, and debug powers
- Emphasis on readable political storytelling, not just mechanical simulation

---
## Techniques

### Systems Design

- Multiple political, military, legal, historical, and UI systems are integrated into one mod architecture
- The project demonstrates the ability to keep complex features coherent over time

### Runtime State Modeling

- Extensive custom runtime extensions for `Kingdom`, `City`, `Actor`, `Empire`, `Title`, and `Faction`
- Rich state synchronization for history, ownership, ideology, occupation, and legitimacy

### Data-Driven Tooling

- Regime configuration, office definitions, localization, and culture bindings are editor-friendly
- Includes a standalone `RegimeEditor` for content workflow support

### UI / UX Work

- Custom windows, map layers, tooltips, nameplates, debug powers, and history presentation
- Focus on making complex systems visible and understandable to users

### AI and Rules

- Extended kingdom AI, empire AI, frontline logic, claim progression, and occupation behavior
- The systems are designed not only to exist in data, but to operate continuously in simulation

## Repository Structure

- [Scripts](/D:/Application/Steam/steamapps/common/worldbox/Mods/EmpireCraft/Scripts)  
  Core gameplay code: AI, UI, Layer systems, GamePatches, GodPowers, helpers, and extensions

- [Locales](/D:/Application/Steam/steamapps/common/worldbox/Mods/EmpireCraft/Locales)  
  Multi-language localization files

- [RegimeEditor](/D:/Application/Steam/steamapps/common/worldbox/Mods/EmpireCraft/RegimeEditor)  
  Standalone regime editor and supporting tools

- [CultureRulesConfig.json](/D:/Application/Steam/steamapps/common/worldbox/Mods/EmpireCraft/CultureRulesConfig.json)  
  Culture-to-regime binding data

- [mod.json](/D:/Application/Steam/steamapps/common/worldbox/Mods/EmpireCraft/mod.json)  
  Mod metadata

---
## Tech Stack

- **Language**: C#
- **Runtime**: Unity / WorldBox Modding Environment
- **Frameworks / Libraries**:
  - NeoModLoader
  - Harmony
  - Newtonsoft.Json
  - DOTween

---
## Installation

1. Install **WorldBox**
2. Make sure your mod environment supports **NeoModLoader**
3. Place this project inside the WorldBox `Mods` directory
4. Launch the game and enable the mod

---
## Current Status

- The project is under active development
- The overall feature set is already broad, while balancing, performance tuning, edge-case fixes, and UI refinement continue over time

---
## Intended Audience

- **Players** looking for deeper imperial politics, legitimacy struggles, and historical progression
- **Recruiters / interviewers** looking for evidence of systems-heavy gameplay engineering, architecture, UI extension, and sustained iteration

---
## Links

- Repository: [EmpireCraft](https://github.com/ZhaoyuZhang101/EmpireCraft)
- License: [LICENSE](/D:/Application/Steam/steamapps/common/worldbox/Mods/EmpireCraft/LICENSE)
