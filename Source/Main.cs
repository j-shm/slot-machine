using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;
using Verse.Noise;
using Verse.Grammar;
using RimWorld;
using RimWorld.Planet;

// *Uncomment for Harmony*
// using System.Reflection;
// using HarmonyLib;

namespace SlotMachine
{

    [StaticConstructorOnStartup]
    public static class Start
    {
        static Start()
        {
            Log.Message("Slots loaded successfully!");
        }
    }


    public static class SlotMachineUtils
    {
        public static int GetTotalSilverOwnedByFaction(Map map, Faction faction)
        {
            if (map == null)
            {
                return 0;
            }

            if (faction == null)
            {
                return 0;
            }

            List<Thing> silverThings = map.listerThings.ThingsOfDef(ThingDefOf.Silver);

            int totalSilver = silverThings
                .Where(silver => silver.Faction == faction || silver.IsInAnyStorage())
                .Sum(silver => silver.stackCount);

            return totalSilver;
        }
    }


    public class MainTabWindow_Slots : MainTabWindow
    {
        private const float ScreenHeightPercent = 0.5f;
        private Rect[] slotRects;
        private int[] slotValues = new int[3];
        private bool[] slotSpinning = new bool[3];
        private int[] slotSpinTime = new int[3];
        private int[] finalSlotValues = new int[3];

        private int betAmount = 0;
        private string betAmountBuffer = "0";

        private int rollTime = 0;
        private int rollDuration = 60;
        private bool winner = false;
        private bool hasRolled = false;

        public override Vector2 InitialSize => new Vector2(UI.screenWidth * 0.5f, UI.screenHeight * ScreenHeightPercent);

        private int failureTicks = 0;

        private string gambleButtonText = "Roll";
        public override void DoWindowContents(Rect inRect)
        {
            if (failureTicks > 0)
            {
                failureTicks--;
            }
            if (failureTicks == 0)
            {
                gambleButtonText = "Roll";
            }
            else
            {
                gambleButtonText = "Not enough silver";
            }

            for (int i = 0; i < slotValues.Length; i++)
            {
                if (slotSpinning[i])
                {
                    slotSpinTime[i]++;
                    if (slotSpinTime[i] >= rollDuration)
                    {
                        slotSpinning[i] = false;
                        slotSpinTime[i] = 0;

                        slotValues[i] = finalSlotValues[i];
                        if (winner)
                        {
                            SoundDef.Named("Quest_Succeded").PlayOneShotOnCamera();
                            if (i == 2)
                            {
                                var stockpileZones = Find.CurrentMap.zoneManager.AllZones
                                    .Where(z => z is Zone_Stockpile)
                                    .Select(z => z as Zone_Stockpile);

                                foreach (var zone in stockpileZones)
                                {
                                    zone.Cells
                                        .Where(c => c.GetFirstThing(Find.CurrentMap, ThingDefOf.Silver) != null)
                                        .ToList()
                                        .ForEach(c =>
                                        {
                                            Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
                                            silver.stackCount = betAmount * finalSlotValues[i];
                                            GenPlace.TryPlaceThing(silver, c, Find.CurrentMap, ThingPlaceMode.Near);
                                        });
                                }
                            }
                        }
                        else
                        {
                            SoundDef.Named("Quest_Failed").PlayOneShotOnCamera();
                        }
                    }
                    else
                    {
                        slotValues[i] = Rand.Range(0, 10);
                    }
                }
            }
            Text.Font = GameFont.Medium;
            Rect titleRect = new Rect(inRect.x, inRect.y, inRect.width, 35f);
            Widgets.Label(titleRect, "Slot Machine");

            Rect slotsOuterRect = new Rect(inRect.x, inRect.y + titleRect.height, inRect.width, inRect.height - titleRect.height - 100f);
            Widgets.DrawBox(slotsOuterRect, thickness: 2);

            slotRects = new Rect[3];
            for (int i = 0; i < slotRects.Length; i++)
            {
                float totalGap = slotsOuterRect.width * 0.1f;
                float slotWidth = (slotsOuterRect.width - totalGap) / 3;
                float slotHeight = slotsOuterRect.height / 3;
                float gap = totalGap / 4;
                float slotX = slotsOuterRect.x + gap + (slotWidth + gap) * i;
                float slotY = slotsOuterRect.y + slotHeight;

                slotRects[i] = new Rect(slotX, slotY, slotWidth, slotHeight);
                Widgets.DrawBox(slotRects[i], thickness: 2);

                if (hasRolled)
                {
                    if (slotValues[i] == finalSlotValues[i])
                    {
                        if (winner)
                        {
                            GUI.color = Color.green;
                        }
                        else
                        {
                            GUI.color = Color.red;
                        }
                    }
                }
                else
                {
                    GUI.color = Color.white;
                }
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(slotRects[i], slotValues[i].ToString());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }

            Rect labelRect = new Rect(inRect.x + 10f, inRect.y + inRect.height - 70f, inRect.width * 0.2f, 30f);
            Rect inputRect = new Rect(inRect.x + 10f, inRect.y + inRect.height - 30f, inRect.width * 0.2f, 30f);
            Rect buttonRect = new Rect(inRect.x + inRect.width * 0.4f, inRect.y + inRect.height - 45f, inRect.width * 0.2f, 30f);

            Widgets.Label(labelRect, "Bet Amount");
            Widgets.TextFieldNumeric(inputRect, ref betAmount, ref betAmountBuffer);
            if (Widgets.ButtonText(buttonRect, gambleButtonText))
            {
                if (betAmount > 0)
                {
                    if (SlotMachineUtils.GetTotalSilverOwnedByFaction(Find.CurrentMap, Faction.OfPlayer) < betAmount)
                    {
                        failureTicks = 60;
                        return;
                    }
                }
                else
                {
                    failureTicks = 60;
                    return;
                }
                var silver = Find.CurrentMap.listerThings.ThingsOfDef(ThingDefOf.Silver).Where(s => s.Faction == Faction.OfPlayer || s.IsInAnyStorage()).ToList();
                var silverRemoved = 0;
                foreach (var thing in silver)
                {
                    if (silverRemoved >= betAmount)
                        break;

                    int removeAmount = (int)Math.Min(thing.stackCount, betAmount - silverRemoved);
                    thing.stackCount -= removeAmount;
                    silverRemoved += removeAmount;

                    if (thing.stackCount <= 0)
                        thing.Destroy(DestroyMode.Vanish);
                }

                int roll = Rand.Range(0, 100);

                int finalValue = 0;

                if (roll < 70)
                {
                    finalValue = 0;
                }
                else if (roll < 75)
                {
                    finalValue = 1;
                }
                else if (roll < 80)
                {
                    finalValue = 2;
                }
                else if (roll < 85)
                {
                    finalValue = 3;
                }
                else if (roll < 90)
                {
                    finalValue = 4;
                }
                else if (roll < 95)
                {
                    finalValue = 5;
                }
                else if (roll < 98)
                {
                    finalValue = 6;
                }
                else if (roll < 99)
                {
                    finalValue = 7;
                }
                else if (roll < 100)
                {
                    finalValue = 8;
                }

                if (finalValue == 0)
                {
                    winner = false;
                    hasRolled = true;
                    List<int> randomNumbers = new List<int>();
                    for (int i = 0; i < 3; i++)
                    {
                        int random = Rand.Range(0, 10);
                        while (randomNumbers.Contains(random))
                        {
                            random = Rand.Range(0, 10);
                        }
                        randomNumbers.Add(random);
                    }
                    finalSlotValues = randomNumbers.ToArray();
                }
                else
                {
                    winner = true;
                    hasRolled = true;
                    for (int i = 0; i < 3; i++)
                    {
                        finalSlotValues[i] = finalValue;
                    }
                }
                rollTime = 0;
                for (int i = 0; i < slotSpinning.Length; i++)
                {
                    slotSpinning[i] = true;
                }
            }
        }

    }
}
