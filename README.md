# Steam App Branch Watcher
Posts updates about the Steam branches of any app to the Discord.

# Using this template

Simply add the right secrets with the right values, and everything should work .

**Note : This code will not work for Steam packages, only Apps.**

You need to create a Steam account for this code to work. The account will be constantly logged in and out, so an active account will probably not work. The account also needs to not be protected by Steam Guard.

The account does not need to own the app to get its info.

### GITHUB SECRETS
- `STEAM_USERNAME` : The username for the Steam account.
- `STEAM_PASSWORD` : The password for the Steam account.
- `DISCORD_WEBHOOK` : The webhook to use for posting to Discord.
- `APPID` : The App ID of the Steam app to watch.
