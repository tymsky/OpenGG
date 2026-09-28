using System.Collections.Generic;

namespace OpenGG.Ui;

/// <summary>
/// The game's sentences: what the dialogs, hints and messages say. OpenGG says everything in its own words; a skin
/// can give other words for any of them by key (skin.json "texts"), for instance the original's, which then stay in
/// the player's own skin. {name} marks what goes in: a part's name, a price, a skill level.
/// </summary>
public static class Words
{
    /// <summary>Every sentence by key, in OpenGG's words.</summary>
    public static readonly IReadOnlyDictionary<string, string> Ours = new Dictionary<string, string>
    {
        // Titles of the dialogs whose words can come from the game (a failed command).
        ["error.title"] = "Error",
        ["assembly.title"] = "Assembly Error!",
        ["cash.title"] = "Not Enough Money",
        ["repair.title"] = "Repair Part",
        ["lotfull.title"] = "Car Lot Full",
        // Failed commands. Without a skin the game's own message is shown, which says more (these are for skins).
        ["cash"] = "You can't pay for that.",
        ["repair.broken"] = "That part is past repairing. Scrap it at the JunkYard for a few dollars.",
        ["repair.good"] = "That part needs no repair.",
        ["remove.first"] = "Take the {parts} off first.",
        ["attach.first"] = "The {parts} must go on first.",
        ["attach.replaces"] = "That part goes where the {part} is. Take it off first.",
        ["attach.worn"] = "That part is too far gone to go back on. Scrap it instead.",
        ["lotfull"] = "There is no room left on your Car Lot. Sell some of your cars at the Auction first.",
        // Questions before spending, and what happened.
        ["buy.part"] = "A new {part} costs {price}. Buy it?",
        ["buy.decal"] = "{uses} {name} decals for {price}. Buy them?",
        ["repair.ask"] = "Fixing up the {part} costs {cost}. Repair it?",
        ["scrap.ask"] = "The JunkYard offers {price} for the {part}. Scrap it?",
        ["auction.car"] = "Your car goes on the block. Once it's up, you only get it back by outbidding everybody. Go ahead?",
        ["auction.won"] = "The car is yours!",
        ["auction.sold.profit"] = "Your car sold for {price}. You made {amount}.",
        ["auction.sold.loss"] = "Your car sold for {price}. You lost {amount}.",
        ["auction.cancelled.title"] = "Sale Called Off",
        ["auction.cancelled"] = "Nobody outbid you for your own car, so the sale is off. The auction house keeps a service fee of {fee}.",
        ["auction.nosale"] = "Nobody bid on your car. It's still yours.",
        // Moving up.
        ["skill.up"] = "Well done! You've gone up from {from} to {to}.",
        ["skill.cars"] = "More cars will turn up at the Auction for you now, and new jobs may come in.",
        ["car.complete"] = "Well done! This car is in top shape now. It took you {time}, and you spent {cost} on it.",
        ["tip.first"] = "You finished your first job! The customer pays the fee, and whatever is left of the job's budget is yours to keep too.",
        ["tip.future"] = "Finish the jobs that come in to become a Novice mechanic. Then you can buy cars at the Auction, fix them up and sell them for a profit.",
        // On the screens.
        ["workshop.empty"] = "There's no car in your WorkShop. Bring one over from the Car Lot or buy one at the Auction.",
        ["bolts.undo"] = "Click the bolts with the left mouse button to take them out.",
        ["bolts.do"] = "Click the holes with the left mouse button to put the bolts in.",
        ["job.begin"] = "Press OK to start on the job.",
        ["job.update.restart"] = "OK to carry on, RESTART to start this job over.",
        ["job.update.cancel"] = "OK to carry on, CANCEL to give up this job.",
        ["job.active"] = "Leave now and this job is off: you'll have to start it over when you come back.",
        ["decals.none"] = "You have no decals yet. The DECALS section of the Catalog sells them.",
        ["prompt.part"] = "Click a part to buy it.",
        ["prompt.car"] = "Click a car to bring it into the WorkShop.",
        ["prompt.bid"] = "Bid before the time runs out.",
        // The sign-in sheet's boxes.
        ["signin.delete"] = "This mechanic and the whole garage will be gone for good. Go ahead?",
        ["signin.exit"] = "Close OpenGG now?",
        // The game folder (OpenGG's own: the original has no such choice).
        ["folder.none"] = "No Gearhead Garage in {folder}. Pick the folder you installed it to, the one with Data\\Cars in it.",
        ["folder.hint"] = "Have Gearhead Garage? Press GAME FOLDER… and pick the folder you installed it to, to play with its cars, jobs, screens and sounds.",
        ["folder.missing"] = "No Gearhead Garage found in {folder}: playing with the placeholders. Press GAME FOLDER… to pick its folder.",
        ["folder.nolook"] = "No screens or sounds of the original (Data\\Gfx24.dat...) in {folder}: OpenGG's own look is used.",
        // The beta without the original's files, said once (OpenGG's own).
        ["beta.note"] = "This is a beta. Without the original game's files OpenGG has one car model and jobs of its own. Have Gearhead Garage? Press GAME FOLDER… and pick its folder.",
    };

    /// <summary>The sentence for <paramref name="key"/>: the skin's words, else OpenGG's, filled in.</summary>
    public static string Get(string key, params (string Name, string Value)[] fill) =>
        Fill(UiSkin.Text(key) ?? Ours.GetValueOrDefault(key) ?? key, fill);

    /// <summary>The skin's words for <paramref name="key"/> if it has them, else <paramref name="ours"/> as it is.</summary>
    public static string Or(string key, string ours, params (string Name, string Value)[] fill) =>
        UiSkin.Text(key) is { } skin ? Fill(skin, fill) : ours;

    static string Fill(string text, (string Name, string Value)[] fill)
    {
        foreach (var (name, value) in fill) text = text.Replace("{" + name + "}", value);
        return text;
    }
}
