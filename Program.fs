open System
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.Hosting
open Microsoft.AspNetCore.Http

// --- Types ---
type GameState = {
    PlayerHp: int
    MonsterHp: int
    MonsterStatus: string // "Normal", "Frozen", "Weakened"
    LastSpellName: string
}

type EventLog = {
    Actor: string // "Player" or "Monster"
    Message: string
    StateAfter: GameState
}

type TurnResult = {
    State: GameState
    Logs: EventLog list
}

type CastRequest() =
    member val Runes: string[] = [||] with get, set

// --- Messages for Agent ---
type GameMessage =
    | GetState of AsyncReplyChannel<GameState>
    | Reset of AsyncReplyChannel<GameState>
    | CastSpell of string[] * AsyncReplyChannel<TurnResult>

// --- Logic ---
let maxPlayerHp = 80

let initialGameState = {
    PlayerHp = maxPlayerHp
    MonsterHp = 150
    MonsterStatus = "Normal"
    LastSpellName = ""
}

let rnd = Random()

// Evaluate the player's spell
let evaluateSpell (runes: string list) (state: GameState) =
    let counts = 
        runes
        |> List.countBy id
        |> Map.ofList

    let countOf rune =
        match Map.tryFind rune counts with
        | Some c -> c
        | None -> 0

    let f = countOf "Fire"
    let i = countOf "Ice"
    let l = countOf "Lightning"

    let mutable playerDmg = 0
    let mutable monsterDmg = 0
    let mutable nextStatus = state.MonsterStatus
    let mutable logs = []
    let mutable spellName = ""

    if f = 3 then
        spellName <- "Inferno"
        monsterDmg <- 25
        playerDmg <- 8
        logs <- ("Player", "Cast Inferno! Dealt 25 damage, but took 8 recoil damage from the intense heat!") :: logs
    elif i = 3 then
        spellName <- "Absolute Zero"
        monsterDmg <- 10
        playerDmg <- 6
        nextStatus <- "Frozen"
        logs <- ("Player", "Cast Absolute Zero! Dealt 10 damage and Frozen the monster, but took 6 recoil damage from frostbite!") :: logs
    elif l = 3 then
        spellName <- "Chain Lightning"
        monsterDmg <- rnd.Next(10, 31)
        playerDmg <- 5
        logs <- ("Player", sprintf "Cast Chain Lightning! Dealt %d damage, but took 5 recoil damage from the shock!" monsterDmg) :: logs
    elif f = 1 && i = 1 && l = 1 then
        spellName <- "Elemental Blast"
        monsterDmg <- 15
        let heal = Math.Min(8, maxPlayerHp - state.PlayerHp)
        playerDmg <- -heal
        logs <- ("Player", sprintf "Cast Elemental Blast! Dealt 15 damage and healed %d HP." heal) :: logs
    elif f = 2 && l = 1 then
        spellName <- "Plasma Strike"
        monsterDmg <- 20
        playerDmg <- 2
        logs <- ("Player", "Cast Plasma Strike! Dealt 20 damage, but took 2 recoil damage.") :: logs
    elif i = 1 && l = 2 then
        spellName <- "Superconductor"
        monsterDmg <- 15
        playerDmg <- 2
        nextStatus <- "Weakened"
        logs <- ("Player", "Cast Superconductor! Dealt 15 damage and Weakened the monster, but took 2 recoil damage.") :: logs
    else
        spellName <- "Basic Attack"
        monsterDmg <- 5
        logs <- ("Player", "Cast Basic Attack. Dealt 5 damage.") :: logs

    (playerDmg, monsterDmg, nextStatus, spellName, logs |> List.rev)


let processMonsterTurn (state: GameState) =
    if state.MonsterHp <= 0 then
        (0, state.MonsterStatus, [])
    else
        match state.MonsterStatus with
        | "Frozen" ->
            (0, "Normal", [("Monster", "Monster is Frozen and skips its turn!")])
        | "Weakened" ->
            let dmg = rnd.Next(1, 6)
            (dmg, "Normal", [("Monster", sprintf "Monster is Weakened and attacks weakly for %d damage!" dmg)])
        | _ -> // Normal
            if state.MonsterHp <= 50 then
                let dmg = rnd.Next(12, 19)
                (dmg, "Normal", [("Monster", sprintf "Monster is ENRAGED and attacks fiercely for %d damage!" dmg)])
            else
                let dmg = rnd.Next(8, 15)
                (dmg, "Normal", [("Monster", sprintf "Monster attacks for %d damage!" dmg)])

// --- Game Agent ---
let gameAgent = MailboxProcessor<GameMessage>.Start(fun inbox ->
    let rec loop state = async {
        let! msg = inbox.Receive()
        match msg with
        | GetState reply ->
            reply.Reply(state)
            return! loop state
        | Reset reply ->
            let newState = initialGameState
            reply.Reply(newState)
            return! loop newState
        | CastSpell (runes, reply) ->
            if state.PlayerHp <= 0 || state.MonsterHp <= 0 then
                reply.Reply({ State = state; Logs = [] })
                return! loop state
            else
                // Player turn
                let pDmgSelf, mDmg, nextStatusFromPlayer, spellName, pLogs = evaluateSpell (List.ofArray runes) state
                
                let finalMDmg, finalPDmg, finalLogs, finalStatus =
                    if state.LastSpellName <> "" && state.LastSpellName = spellName then
                        (0, 10, [("Player", sprintf "You tried to cast %s again, but the Monster ADAPTED and blocked it! You took 10 counter damage!" spellName)], "Normal")
                    else
                        (mDmg, pDmgSelf, pLogs, nextStatusFromPlayer)

                let stateAfterPlayer = {
                    state with 
                        PlayerHp = Math.Clamp(state.PlayerHp - finalPDmg, 0, maxPlayerHp)
                        MonsterHp = Math.Max(0, state.MonsterHp - finalMDmg)
                        MonsterStatus = finalStatus
                        LastSpellName = spellName
                }

                let pLogEvents = finalLogs |> List.map (fun (a, m) -> { Actor = a; Message = m; StateAfter = stateAfterPlayer })

                // Monster turn
                let mDmgToPlayer, nextStatusFromMonster, mLogs = processMonsterTurn stateAfterPlayer

                let finalState = {
                    stateAfterPlayer with
                        PlayerHp = Math.Max(0, stateAfterPlayer.PlayerHp - mDmgToPlayer)
                        MonsterStatus = nextStatusFromMonster
                }

                let mLogEvents = mLogs |> List.map (fun (a, m) -> { Actor = a; Message = m; StateAfter = finalState })

                reply.Reply({ State = finalState; Logs = pLogEvents @ mLogEvents })
                return! loop finalState
    }
    loop initialGameState
)

// --- Web API ---
[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder(args)
    let app = builder.Build()

    // Serve static files from wwwroot
    app.UseDefaultFiles() |> ignore
    app.UseStaticFiles() |> ignore

    app.MapGet("/api/state", Func<GameState>(fun () ->
        gameAgent.PostAndReply(GetState)
    )) |> ignore

    app.MapPost("/api/cast", Func<CastRequest, TurnResult>(fun req ->
        let reqObj = box req
        let runes = if isNull reqObj || isNull req.Runes then [||] else req.Runes
        gameAgent.PostAndReply(fun reply -> CastSpell(runes, reply))
    )) |> ignore

    app.MapPost("/api/reset", Func<GameState>(fun () ->
        gameAgent.PostAndReply(Reset)
    )) |> ignore

    app.Run()
    0
