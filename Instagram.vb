Imports Testagram.ProfileData
Imports InstagramApiSharp.API
Imports InstagramApiSharp.Classes
Imports InstagramApiSharp.Classes.Models
Imports System.Collections.ObjectModel
Imports System.Net.Http
Imports System.IO
Imports TwoFactorAuthNet

Public Class Instagram
    Private Const DEFAULT_RATELIMIT_BACKOFF_PERIOD As Integer = 20

    Private pUserCreds As New UserSessionData
    Private pTwoFactorSecret As String
    Private pApi As IInstaApi

    Sub New(ByVal Username As String, ByVal Password As String, ByVal TwoFactorSecret As String)
        pUserCreds.UserName = Username
        pUserCreds.Password = Password
        pTwoFactorSecret = TwoFactorSecret
    End Sub

    Public Function AttemptLogin() As Boolean
        Return AttemptLoginAsync().ConfigureAwait(False).GetAwaiter().GetResult()
    End Function

    Public Async Function AttemptLoginAsync() As Task(Of Boolean)
        Dim B As Builder.InstaApiBuilder = InstagramApiSharp.API.Builder.InstaApiBuilder.CreateBuilder()
        Call B.SetUser(pUserCreds)
        pApi = B.Build()

        Call LogConsole("[*] Initial Login attempt...")

        Dim LoginRes As IResult(Of InstaLoginResult)

        If Not LoadSession() = "" Then
            Call pApi.LoadStateDataFromString(LoadSession())
            If pApi.IsUserAuthenticated Then
                Return True
            End If
        End If

        LoginRes = Await pApi.LoginAsync()

        If LoginRes.Succeeded Then
            If LoginRes.Value = InstaLoginResult.Success Then
                Call LogConsole("[+] Login succeeded!", ConsoleColor.Green)
                Call SaveSession(pApi.GetStateDataAsString())
                Return True
            End If
            Stop
        Else
            If LoginRes.Value = InstaLoginResult.TwoFactorRequired Then
                Dim TFACode As String
                Dim TwoFactorInfo As IResult(Of InstaTwoFactorLoginInfo) = Await pApi.GetTwoFactorInfoAsync()
                Dim TwoFactorLoginRes As IResult(Of InstaLoginTwoFactorResult)
                If TwoFactorInfo.Succeeded Then
                    Select Case True
                        Case TwoFactorInfo.Value.SmsTwoFactorOn
                            ' Do SMS 2FA code here
                        Case TwoFactorInfo.Value.ToTpTwoFactorOn
                            Dim TFA As New TwoFactorAuth
                            If pTwoFactorSecret <> "" Then
                                TFACode = TFA.GetCode(pTwoFactorSecret)
                                TwoFactorLoginRes = Await pApi.TwoFactorLoginAsync(TFACode)

                                Do While (TwoFactorLoginRes.Value = InstaLoginTwoFactorResult.CodeExpired)
                                    Call LogConsole("[!] Code expired, waiting 30 seconds...", ConsoleColor.Yellow)
                                    Await Task.Delay(30000)
                                    TwoFactorLoginRes = Await pApi.TwoFactorLoginAsync(TFA.GetCode(pTwoFactorSecret))
                                Loop

                                If TwoFactorLoginRes.Succeeded Then
                                    Call LogConsole("[+] Login succeeded (TOTP 2FA flow)!", ConsoleColor.Green)
                                    Call SaveSession(pApi.GetStateDataAsString())
                                    Return True
                                End If
                            Else
                                Call LogConsole("[-] Login failure (2FA TOTP Flow requested, TOTP Private Key not loaded)", ConsoleColor.Red)
                            End If
                    End Select
                Else
                    Stop
                End If

                Stop
            ElseIf LoginRes.Value = InstaLoginResult.ChallengeRequired Then
                Dim ChallengeRes As IResult(Of InstaChallengeRequireVerifyMethod) = Await pApi.GetChallengeRequireVerifyMethodAsync()
                If ChallengeRes.Succeeded Then
                    Call LogConsole("[*] Email Challenge flow")
                    Dim EmailChallengeRes As IResult(Of InstaChallengeRequireEmailVerify) = Await pApi.RequestVerifyCodeToEmailForChallengeRequireAsync()
                    If EmailChallengeRes.Succeeded Then
                        Console.Write("[?] Please enter email verification code: ")
                        Dim ChallengeCode As String = Console.ReadLine()
                        LoginRes = Await pApi.VerifyCodeForChallengeRequireAsync(ChallengeCode)
                        If LoginRes.Succeeded Then
                            If LoginRes.Value = InstaLoginResult.Success Then
                                Call LogConsole("[+] Login success!", ConsoleColor.Green)
                                Call SaveSession(pApi.GetStateDataAsString())
                                Return True
                            ElseIf LoginRes.Value = InstaLoginResult.ChallengeRequired Then
                                Dim ChallengeInfoRes As IResult(Of InstaLoggedInChallengeDataInfo) = Await pApi.GetLoggedInChallengeDataInfoAsync
                                If ChallengeInfoRes.Succeeded Then
                                    Dim AcceptChallengeRes As IResult(Of Boolean) = Await pApi.AcceptChallengeAsync()
                                    If AcceptChallengeRes.Succeeded Then
                                        If AcceptChallengeRes.Value Then
                                            Call LogConsole("[+] Login success (Accept Challenge flow)!", ConsoleColor.Green)
                                            Call SaveSession(pApi.GetStateDataAsString())
                                            Return True
                                        End If
                                    End If
                                End If

                                Call LogConsole("[-] Login temporarily disabled, wait and try again later.", ConsoleColor.Red)
                                Return False
                            Else
                                Stop
                            End If
                        Else
                            Stop
                        End If
                    Else
                        Stop
                    End If
                Else
                    Stop
                End If
            ElseIf LoginRes.Value = InstaLoginResult.LimitError Then
                Stop
            Else
                Stop
            End If
        End If

        Return False
    End Function

    Public Function Logout() As Boolean
        Return LogoutAsync().ConfigureAwait(False).GetAwaiter().GetResult()
    End Function

    Public Async Function LogoutAsync() As Task(Of Boolean)
        If (Await pApi.LogoutAsync()).Succeeded Then
            Return True
        End If

        Return False
    End Function

    Public Function GetFollowing() As Collection(Of String)
        Return GetFollowingAsync().ConfigureAwait(False).GetAwaiter().GetResult()
    End Function

    Public Async Function GetFollowingAsync() As Task(Of Collection(Of String))
        Call LogConsole("[*] Enumerating Followed Users...")
        Dim RateLimitDelaySeconds As Integer = DEFAULT_RATELIMIT_BACKOFF_PERIOD
        Dim LastPercentDisplayed As Integer = -1
        Dim Paging As InstagramApiSharp.PaginationParameters = InstagramApiSharp.PaginationParameters.MaxPagesToLoad(100)
        Dim FollowingResult As IResult(Of InstaUserShortList) = Await pApi.UserProcessor.GetUserFollowingAsync(pApi.GetLoggedUser().UserName, Paging)
        If FollowingResult.Succeeded Then
            Dim Following As InstaUserShortList = FollowingResult.Value
            Dim Profiles As New Collection(Of String)
            Dim HttpClient As New HttpClient

            If Not Following Is Nothing Then
                For i = 0 To Following.Count - 1
                    Dim UIResult As IResult(Of InstaUserInfo) = Await pApi.UserProcessor.GetUserInfoByIdAsync(Following(i).Pk)
                    If UIResult.Succeeded Then
                        Dim Profile As String = UIResult.Value.Pk

                        Call Profiles.Add(Profile)
                    End If
                Next

                Call LogConsole("[*] Enumeration complete.")
                Return Profiles
            End If
        End If

        Call LogConsole("[-] Couldn't access index of Followed users.", ConsoleColor.Red)
        Return Nothing
    End Function

    Private Sub LogConsole(ByVal LogLine As String, Optional ByVal Color As ConsoleColor = Nothing)
        Dim PrevColor As ConsoleColor = Console.ForegroundColor
        If Not Color = Nothing Then
            Console.ForegroundColor = Color
        End If
        Call Console.WriteLine(LogLine)
        Console.ForegroundColor = PrevColor
    End Sub

    Private Sub SaveSession(ByVal SessionData As String)
        FileIO.FileSystem.WriteAllText("state.bin", SessionData, False)
    End Sub

    Private Function LoadSession() As String
        Try
            Return FileIO.FileSystem.ReadAllText("state.bin")
        Catch ex As Exception
            Return ""
        End Try
    End Function
End Class
