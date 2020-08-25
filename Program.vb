Imports System
Imports System.Collections.ObjectModel
Imports TwoFactorAuthNet

Module Program
    Sub Main(args As String())
        Dim PrivateKey As String = "R3NSRIREIT4CNKHR7CSHM7HZAPXEXEMI"
        Dim Instagram As New Instagram("jbellomy97", "a12345678", PrivateKey)

        If PrivateKey <> "" Then
            Dim TFA As New TwoFactorAuth
            Dim Code As String = TFA.GetCode(PrivateKey)
            Call Console.WriteLine("[*] TOTP 2FA Code: " & Code)
        End If

        If Instagram.AttemptLogin() Then
            Dim Profiles As Collection(Of String) = Instagram.GetFollowing()
            If Not Profiles Is Nothing Then
                Call Console.WriteLine("[*] Obtained followers.")
            Else
                Call Console.WriteLine("[-] Failed.")
            End If

            Call Instagram.Logout()
        End If
    End Sub
End Module
