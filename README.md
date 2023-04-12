# Outer Wilds Branch Watcher
Posts updates about the Steam branches of Outer Wilds to the modding discord.

## Extending this for other games

Using this code for other games / discord servers is easy! Just change the app id in the code to the app you want to watch, and change the guild/channel id to point to the channel you want.

**Note : This code will not work for Steam packages, only Apps.**

You need to create a Steam account for this code to work. The account will be constantly logged in and out, so an active account will probably not work. The account also needs to not be protected by Steam Guard.

The account does not need to own the app to get it's info.

### GITHUB SECRETS
- `STEAM_USERNAME` : The username for the Steam account.
- `STEAM_PASSWORD` : The password for the Steam account.
- `BOT_TOKEN` : The token for the Discord bot.
