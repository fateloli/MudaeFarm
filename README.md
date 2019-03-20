# MudaeFarm

**Warning**: selfbots are officially banned. It is considered an API abuse and is [no longer tolerated](https://support.discordapp.com/hc/en-us/articles/115002192352-Automated-user-accounts-self-bots-).

~~So please don't be caught using this bot.~~

This is a simple bot I wrote in a few hours that automatically rolls and claims Mudae waifus/husbandos.

## Setup

1. Install [.NET Core SDK](https://dotnet.microsoft.com/download).

2. Download this repository [as a zip](https://github.com/phosphene47/MudaeFarm/archive/master.zip).

3. Once the file is downloaded, unzip it. Move into the folder named `MudaeFarm` and create a file called `config.json` with the following contents:

```json
{
  "AuthToken": "INSERT YOUR TOKEN HERE"
}
```

You can find your user token: press Ctrl+Shift+I or Command+Option+I while on Discord which opens Chrome inspector then click on the tab named `Network`. Click on `XHR`.

![xhr](images/xhr.png)

Open a channel on Discord which should create a request called `science`.

![science](images/science.png)

Under `Request Headers` find your authorization token and copy that.

![headers](images/headers.png)

DO NOT EVER SHARE THIS TOKEN WITH ANYONE. SHARING THIS TOKEN IS SHARING YOUR PASSWORD.

4. Open terminal or cmd in the folder and run the following commands: `dotnet build`, `dotnet run`

### Note

This bot does not automatically know which Mudae maid you are using. You may need to copy the ID of Mudae maid bot and add it in `MudaeFarm/Program.cs`:

```public static ulong[] MudaeIds = new ulong[]
{
    PASTE_MUDAE_MAID_ID_HERE
};
```

## Commands

Autoclaiming:

- `/wish {character}` — Adds a character to your wishlist.
- `/unwish {character}` — Removes a character from your wishlist.
- `/wishani {anime}` — Adds an anime to your wishlist. This is akin to wishing every character from that anime.
- `/unwishani {anime}` — Removes an anime from your wishlist.
- `/wishlist` — Shows the list of your wished characters.
- `/wishlistani` — Shows the list of your wished anime.

Autorolling:

- `/rollinterval {minutes}` — Sets the autoroll interval in minutes. Setting this to `-1` disables autorolling.
- `/setchannel` — Sets the channel where you send this command as a bot channel. You can do this in however many channels as you like.
- `/unsetchannel` — Stops autorolling in the channel where you send this command.
- '/marry waifu' — Sets marry target to waifus (`$w`). This is the default roll command.
- '/marry husbando' — Sets marry target husbandoes (`$h`).
