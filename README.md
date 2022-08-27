# **PortfolioHelper**
**This project provides services to indipendently manage a stock portfolio held with Interactive Brokers, through an interactive Telegram bot.**<br><br>

Third parties services used by this program --><br>
[Interactive brokers](https://www.interactivebrokers.com/en/home.php) <img src="https://user-images.githubusercontent.com/96583994/186845798-c61589e7-98e0-44e1-98fc-0c1aa3b66fa4.png" width="20"> (from now on "IB").<br>
[Telegram](https://telegram.org) <img src="https://user-images.githubusercontent.com/96583994/186847627-623cce7a-16d6-40e3-a53e-bea732cd774d.png" width="20"> (referred to as well as the "bot" since it's the functionality we're going to use).<br>
[Alpha Vantage](https://www.alphavantage.co) <img src="https://user-images.githubusercontent.com/96583994/186850670-4af2a699-2c42-466d-9b68-778bf8c80600.jpg" width="20"> (from now on "AV").<br><br>

All the third party services here mentioned offer at least a **free** option, and this project can be used with both free and paid options.
<br>
EG Alpha Vantage offer a paid API access with a higher cap of requests per minute, you can change a parameter in the ".env" configuration file accordingly and the program will automatically adjust.<br><br>
* IB --> 
    1. Open a [free IB account](https://www.interactivebrokers.com/en/pagemap/pagemap_newaccounts_v3.php).
    2. Dowload either the [Gateway](https://www.interactivebrokers.com/en/trading/ibgateway-stable.php) or the [Trader Workstation](https://www.interactivebrokers.com/en/trading/tws-updateable-latest.php) client platform. If you're unsure see the <sup>[**IB: Gateway or Trader Workstation?**](#gw_or_tws)</sup> section.
* Telegram --> 
    1. If you don't have a bot account you can create a new one (to associate with this program) by asking Telegram's [BotFather](https://t.me/botfather) the "/newbot" command. Put this information in the ".env" file.
    2. It is possible to get Telegram chat id by asking [IDBot](https://telegram.me/myidbot) the "/getid" command, this will be used to only allow your account to interact with the bot. Put this information in the ".env" file.
* Alpha Vantage -->
    1. Read the [documentation](https://www.alphavantage.co/documentation/).
    2. Receive a [free API key](https://www.alphavantage.co/support/#api-key).

**NOTE:** this project is in no way affiliated with the third party services mentioned it only interfaces with their public facing APIs. I earn no commission based on your use of this program or the services mentioned and they have been selected on the basis that I find them valuable.<br><br>


-------------------------------------
<br>

This program is **multi platform** and all the third party services have been chosen because they also provide multi platform support.<br>
Compiled versions for **Windows, MacOs, and Linux** are available to download.<br><br>

THe program uses 3 main routines, one manages the **Telegram bot** and through many commands that it is listening for, many of which are described below. The second manages the **alarms**, logic is included to allow for many orders possibilities (also described below). The third is a **notifier** that inform the user of portfolio change or orded triggers.

- All the core logic is written in **C#**, using **.NET 6.0**. 
- **REST** commands are used to interact with third parties APIs. 
- All the routines mentioned run concurrently in a **multi-hreading thread safe** manner.
- A **persistent database** allows for program start and stop with **no data loss**.
- **Python** modules are used to interface with the IB client software, these calls are **managed by the main .NET** routines to never cross each other and (access to the same resource) and **safely release resources when done**.
- A thorough **log** system paired with extensive and descriptive **error handling** allows for safe exwcution even in case of errors (network, disk..) and the possibility to remotely check the logs through the bot.
- **Intelligent resource management** is used. Logic is implemented to take "breaks" during some operations to not needlessly waste resources, EG if the API allows for 5 calls per minute, 5 calls are spaced every 12 seconds (12*5=60) not not saturate the quota in the first 5 seconds and run into API limits for the next 55 seconds.
- Cosidered the relative small size of the program SQLite was used for persistency, althouth a more sizeable DB could be used as well.
- At the project design phase it was decided to opt for a **functional programming** approach to have the program be less resource intensive, most classes are in fact static. In case support for different DBs would be required this could be implemented by adding the needed methods to a new class, or redesigning some parts to use a common interface and inversion of control (IoC) at instance creation, both methods work.

<br><br>

Excluding the runtime itself, the program is quite inexpensive in resources --><br>
<img src="https://user-images.githubusercontent.com/96583994/187036971-5ad57797-dfa5-461e-9c6b-3bc6a1740d1e.png" width="650"><br><br>

-------------------------------------
<br>

Launch the program passing the "**paper**" or "**live**" parameter, at first launch an ".env" file template will be created --><br>
<img src="https://user-images.githubusercontent.com/96583994/186874924-a9caa6f5-9fb0-466e-897f-825c94f752be.png" width="650"><br><br>
".env" template (OPTIONAL parameters can be removed) --><br>
<img src="https://user-images.githubusercontent.com/96583994/186875272-56af45d6-523e-467b-bebd-daa33091c6e7.png" width="500"><br>

The program uses the IB client software to connect to your account, **please make sure either Gateway or Trader Workstation is running**.<br>

Once configured and lauched, you don't need to do anything else on the host machine!<br>
**This could be running on a remote machine and from now on only controlled through the Telegram bot.**<br><br>
At launch, the user is greeted by a confirmation message that everything has been launched correctly and a first step instruction if in doubt--><br>
<img src="https://user-images.githubusercontent.com/96583994/186894254-821a56ff-443a-45b5-aad3-846f273f3205.png" width="500"><br><br>
From here many paths open, you can request general **info** --><br>
<img src="https://user-images.githubusercontent.com/96583994/186894508-6662e4bc-b938-46fe-afee-459b19a64297.png" width="500"><br><br>
or **check** the status of the applcation --><br>
<img src="https://user-images.githubusercontent.com/96583994/186894630-48705aee-e09e-41f1-a215-4bf8444172dd.png" width="500"><br><br>
The **check** command shows if any blocking errors occurred and advices the user to restart the application, the **restart** command allows to launch a safe restart of all services with **no data loss** --><br>
<img src="https://user-images.githubusercontent.com/96583994/186895033-ebbc764c-5cab-4da6-a6dd-5b1106fc2786.png" width="500"><br><br>
The only error that triggers an automatic safe restart is a Telegram bot error, this was considered ok since this program is desivgned to be used solely with the bot as interface and a bot blocking error would damage the user experience.<br><br>

**example** command is available to guide the user --><br>
<img src="https://user-images.githubusercontent.com/96583994/186895966-7b6523cc-bd50-4d2b-9fd2-52e9bb994e8c.png" width="500"><br><br>
as well as the **describe** command with a description for every available command --><br>
<img src="https://user-images.githubusercontent.com/96583994/186895686-c3a405ea-40a2-4d02-b307-742f1c46c9ab.png" width="500"><br><br>

A **limitless amount of alarms** can be created for any amount of stocks, there is also support for custom indicators if the user decides to implement them --><br>
(Here a the alarm monitors if Microsoft's price cross up $100, and when that happens 500 shares will be sold)<br>
<img src="https://user-images.githubusercontent.com/96583994/186896401-ef89211a-79e3-4eb6-8bd6-ee96c1ec3e9e.png" width="500"><br><br>
The user is **notified when an order set through an alarm is triggered** and the portfolio position changes --><br>
<img src="https://user-images.githubusercontent.com/96583994/186897413-05a3f5e0-e682-48fe-9ecb-9257abfd4b06.png" width="650"><br><br>

It is also possible to **check the portfolio status at any time** --><br>
<img src="https://user-images.githubusercontent.com/96583994/186898376-2e7705d1-f362-43b3-b63d-1a89cd2a1de8.png" width="500"><br><br>

And manage order, EG **cancel** them --><br>
<img src="https://user-images.githubusercontent.com/96583994/186898547-06758482-623c-4020-a7f5-d4c993419dcf.png" width="500"><br><br>

Please remember this application requires that the IB client is running --><br>
<img src="https://user-images.githubusercontent.com/96583994/186898782-a1dfe69c-383a-4ca3-bd6b-5e83e54fa9ba.png" width="500"><br><br>

Still, even in case of error the user is protected from notification flooding --><br>
<img src="https://user-images.githubusercontent.com/96583994/186899245-6e15371d-4c23-4319-8bbf-991272fd5fce.png" width="500"><br><br>

-------------------------------------
<br>

<a name="further_info">**FURTHER INFO**</a><br><br>

<a name="gw_or_tws">**IB: Gateway or Trader Workstation?**</a><br>
    Gateway client at hidle uses routhly 35% less memory than Trader Workstation --><br>
    <img src="https://user-images.githubusercontent.com/96583994/186860541-7c5c2b77-e50b-489e-b2e8-6a4d0ed2319c.png" width="650"><br><br>
    But it offers no UI experience --><br>
    <img src="https://user-images.githubusercontent.com/96583994/186865973-ff1ff918-cd9f-49ff-b4dd-0435d8f6952a.png" width="650"><br><br><br>
    **Which one should you choose?**<br>
    If you're going to use the client only as an interface to allow access to your account then **Gateway** makes more sense, if otherwise you're going to use the UI client functionalities then **Trader Workstation** allows you to also do that.
    
-------------------------------------
<br>

<a name="references">**REFERENCES**</a><br><br>

This program uses the [Telegram.Bot](https://github.com/TelegramBots/Telegram.Bot) library for many .NET client Telegram bot features, the [ib_insync](https://github.com/erdewit/ib_insync) library for Python for many operations managing the IB account.

-------------------------------------
<br>

**NOTE:** USE AT YOUR OWN RISK. This software has been tested on paper account so no real money has been used, the author is not to be credited with money gained or considered responsible with losses or the software not working as intended.<br>
Do your own researh, be thoughtful, and be safe.

