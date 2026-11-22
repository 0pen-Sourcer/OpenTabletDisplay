param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase
. (Join-Path $PSScriptRoot 'common.ps1')

$script:AppVersion = $script:FSDLVersion
$script:InstallProcess = $null
$script:DiagnosticProcess = $null
$script:Installing = $false
$script:InstallFinished = $false
$script:UsbProcess = $null
$script:UsbOut = Join-Path $script:InstallRoot 'logs\usb-check.txt'
$script:UsbErr = Join-Path $script:InstallRoot 'logs\usb-check-error.txt'
$script:LastInstallMessage = ''
$script:LastActivity = ''
$script:LastDiagnosticText = ''
$script:CurrentPage = 'Overview'
$script:InstallLog = Join-Path $script:InstallRoot 'logs\install.log'
$script:AgentLog = Join-Path $script:InstallRoot 'runtime\agent.log'
$script:DiagOut = Join-Path $script:InstallRoot 'logs\desktop-doctor.txt'
$script:DiagErr = Join-Path $script:InstallRoot 'logs\desktop-doctor-error.txt'
$script:RuntimeExe = Join-Path $script:InstallRoot 'FSDL.exe'

function Get-InstallerScript {
    $local = Join-Path $PSScriptRoot 'install.ps1'
    if (Test-Path -LiteralPath $local) { return $local }
    $saved = Join-Path $script:InstallRoot 'package\installer\install.ps1'
    if (Test-Path -LiteralPath $saved) { return $saved }
    return $null
}

$xaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="OpenTabletDisplay" Width="1130" Height="770" MinWidth="980" MinHeight="675"
        WindowStartupLocation="CenterScreen" Background="#0A101A" Foreground="#EFF4FC"
        FontFamily="Segoe UI" UseLayoutRounding="True" SnapsToDevicePixels="True">
  <Window.Resources>
    <Style x:Key="PrimaryButton" TargetType="Button">
      <Setter Property="FontFamily" Value="Segoe UI"/>
      <Setter Property="FontWeight" Value="SemiBold"/>
      <Setter Property="FontSize" Value="13"/>
      <Setter Property="Foreground" Value="#FFFFFF"/>
      <Setter Property="Background" Value="#427FF5"/>
      <Setter Property="Cursor" Value="Hand"/>
      <Setter Property="Padding" Value="18,12"/>
      <Setter Property="MinHeight" Value="42"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="Button">
            <Border x:Name="Surface" Background="{TemplateBinding Background}" CornerRadius="10" Padding="{TemplateBinding Padding}">
              <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Surface" Property="Opacity" Value="0.87"/></Trigger>
              <Trigger Property="IsEnabled" Value="False"><Setter TargetName="Surface" Property="Opacity" Value="0.38"/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key="SecondaryButton" TargetType="Button" BasedOn="{StaticResource PrimaryButton}">
      <Setter Property="Background" Value="#253349"/>
      <Setter Property="Foreground" Value="#E9F0FC"/>
    </Style>
    <Style x:Key="NavButton" TargetType="Button">
      <Setter Property="Height" Value="46"/>
      <Setter Property="FontSize" Value="14"/>
      <Setter Property="FontWeight" Value="SemiBold"/>
      <Setter Property="Foreground" Value="#9EACBF"/>
      <Setter Property="Background" Value="Transparent"/>
      <Setter Property="Cursor" Value="Hand"/>
      <Setter Property="HorizontalContentAlignment" Value="Left"/>
      <Setter Property="Margin" Value="0,0,0,5"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="Button">
            <Border x:Name="Surface" Background="{TemplateBinding Background}" CornerRadius="10" Padding="17,0">
              <ContentPresenter VerticalAlignment="Center" HorizontalAlignment="Left"/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Surface" Property="Background" Value="#202C3E"/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key="Eyebrow" TargetType="TextBlock">
      <Setter Property="Foreground" Value="#8AA6D2"/>
      <Setter Property="FontSize" Value="11"/>
      <Setter Property="FontWeight" Value="Bold"/>
    </Style>
    <Style x:Key="Heading" TargetType="TextBlock">
      <Setter Property="Foreground" Value="#F4F8FF"/>
      <Setter Property="FontSize" Value="21"/>
      <Setter Property="FontWeight" Value="SemiBold"/>
    </Style>
    <Style x:Key="Muted" TargetType="TextBlock">
      <Setter Property="Foreground" Value="#93A5BD"/>
      <Setter Property="FontSize" Value="13"/>
      <Setter Property="TextWrapping" Value="Wrap"/>
    </Style>
    <Style x:Key="Card" TargetType="Border">
      <Setter Property="Background" Value="#131E2C"/>
      <Setter Property="BorderBrush" Value="#26354A"/>
      <Setter Property="BorderThickness" Value="1"/>
      <Setter Property="CornerRadius" Value="15"/>
      <Setter Property="Padding" Value="21"/>
    </Style>
  </Window.Resources>
  <Grid>
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width="242"/>
      <ColumnDefinition Width="*"/>
    </Grid.ColumnDefinitions>
    <Border Grid.Column="0" Background="#101926" BorderBrush="#213147" BorderThickness="0,0,1,0">
      <DockPanel LastChildFill="True" Margin="18,24,18,22">
        <StackPanel DockPanel.Dock="Top">
          <StackPanel Orientation="Horizontal" Margin="7,2,0,40" VerticalAlignment="Center">
            <Border Background="#397CF4" Width="38" Height="38" CornerRadius="11">
              <TextBlock Text="▣" Foreground="White" FontSize="25" FontWeight="Bold" HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
            <StackPanel Margin="12,0,0,0" VerticalAlignment="Center">
              <TextBlock Text="OpenTablet" Foreground="#F5F8FF" FontSize="17" FontWeight="Bold"/>
              <TextBlock Text="DISPLAY" Foreground="#679DFB" FontSize="9" FontWeight="Bold"/>
            </StackPanel>
          </StackPanel>
          <TextBlock Text="WORKSPACE" Foreground="#6E829A" FontSize="10" FontWeight="Bold" Margin="17,0,0,12"/>
          <Button x:Name="NavOverview" Content="◉   Overview" Style="{StaticResource NavButton}" Background="#243A59" Foreground="#FFFFFF"/>
          <Button x:Name="NavConnect" Content="＋   Set up &amp; connect" Style="{StaticResource NavButton}"/>
          <Button x:Name="NavDiagnostics" Content="≡   Diagnostics" Style="{StaticResource NavButton}"/>
          <Button x:Name="NavSettings" Content="⚙   Preferences &amp; help" Style="{StaticResource NavButton}"/>
          <Border Background="#1A2A3F" BorderBrush="#304763" BorderThickness="1" CornerRadius="13" Padding="14" Margin="0,34,0,0">
            <StackPanel>
              <TextBlock Text="WIRED BY DESIGN" Foreground="#80ABF9" FontSize="10" FontWeight="Bold"/>
              <TextBlock Text="Your USB cable carries the display stream." TextWrapping="Wrap" Margin="0,8,0,5" FontSize="13" Foreground="#E5EDFA"/>
              <TextBlock Text="No cloud account or subscription." Style="{StaticResource Muted}" FontSize="11"/>
            </StackPanel>
          </Border>
        </StackPanel>
        <StackPanel DockPanel.Dock="Bottom" VerticalAlignment="Bottom">
          <Border Background="#172231" CornerRadius="8" Padding="10,9">
            <StackPanel Orientation="Horizontal">
              <Ellipse x:Name="SidebarDot" Fill="#F4B95A" Width="9" Height="9" Margin="0,4,9,0"/>
              <TextBlock x:Name="SidebarState" Text="Checking system" Foreground="#D1DEEF" FontSize="12"/>
            </StackPanel>
          </Border>
          <TextBlock Text="Windows 10 / 11  •  v3.2 RC" Foreground="#63758B" FontSize="11" Margin="6,14,0,0"/>
        </StackPanel>
      </DockPanel>
    </Border>
    <Grid Grid.Column="1">
      <Grid.RowDefinitions>
        <RowDefinition Height="*"/>
        <RowDefinition Height="Auto"/>
      </Grid.RowDefinitions>
      <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
        <StackPanel Margin="36,30,36,26">
          <Grid Margin="0,0,0,27">
            <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
            <StackPanel>
              <TextBlock x:Name="PageEyebrow" Text="YOUR WORKSPACE" Style="{StaticResource Eyebrow}"/>
              <TextBlock x:Name="PageHeading" Text="Overview" Foreground="White" FontSize="29" FontWeight="SemiBold" Margin="0,5,0,4"/>
              <TextBlock x:Name="PageSubheading" Text="A second monitor, without a second monitor." Style="{StaticResource Muted}"/>
            </StackPanel>
            <Button x:Name="RefreshButton" Grid.Column="1" Style="{StaticResource SecondaryButton}" Content="↻  Refresh status" VerticalAlignment="Center" Margin="18,0,0,0"/>
          </Grid>

          <StackPanel x:Name="OverviewPage">
            <Border Background="#172842" CornerRadius="20" BorderBrush="#2D4B76" BorderThickness="1" Padding="27" Margin="0,0,0,20">
              <Grid>
                <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                <StackPanel Margin="0,0,16,0">
                  <TextBlock Text="OPEN TABLET DISPLAY" Style="{StaticResource Eyebrow}" Foreground="#8FB8FF"/>
                  <TextBlock x:Name="HeroTitle" Text="Make room for more." Foreground="#FFFFFF" FontSize="27" FontWeight="SemiBold" TextWrapping="Wrap" Margin="0,10,0,10"/>
                  <TextBlock x:Name="HeroDescription" Text="Connect an Android tablet or phone to make it a real extended Windows monitor." Style="{StaticResource Muted}" Foreground="#BDCEE6" MaxWidth="440" FontSize="14"/>
                  <StackPanel Orientation="Horizontal" Margin="0,20,0,0">
                    <Button x:Name="HeroSetup" Content="Set up my display" Style="{StaticResource PrimaryButton}" Margin="0,0,10,0"/>
                    <Button x:Name="HeroDetails" Content="View diagnostics" Style="{StaticResource SecondaryButton}"/>
                  </StackPanel>
                </StackPanel>
                <Border Grid.Column="1" Width="134" Height="104" CornerRadius="15" Background="#244674" VerticalAlignment="Center">
                  <Grid Margin="18">
                    <Border CornerRadius="7" BorderThickness="3" BorderBrush="#98C2FF" Margin="0,0,24,20"/>
                    <Border CornerRadius="5" BorderThickness="3" BorderBrush="#FFFFFF" Background="#3566AB" Width="47" Height="68" HorizontalAlignment="Right" VerticalAlignment="Bottom"/>
                    <Border Width="7" Height="7" CornerRadius="4" Background="#9FD9FF" HorizontalAlignment="Right" VerticalAlignment="Bottom" Margin="0,0,20,7"/>
                  </Grid>
                </Border>
              </Grid>
            </Border>
            <TextBlock Text="SYSTEM HEALTH" Style="{StaticResource Eyebrow}" Margin="0,2,0,12"/>
            <Grid Margin="0,0,0,22">
              <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
              <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
              <Border Grid.Row="0" Grid.Column="0" Style="{StaticResource Card}" Margin="0,0,7,14">
                <StackPanel>
                  <TextBlock Text="APPLICATION" Style="{StaticResource Eyebrow}"/>
                  <TextBlock x:Name="AppStatus" Text="Checking" FontSize="20" FontWeight="SemiBold" Foreground="White" Margin="0,8,0,4"/>
                  <TextBlock x:Name="AppDetail" Text="Inspecting installation state" Style="{StaticResource Muted}"/>
                </StackPanel>
              </Border>
              <Border Grid.Row="0" Grid.Column="1" Style="{StaticResource Card}" Margin="7,0,0,14">
                <StackPanel>
                  <TextBlock Text="SUNSHINE" Style="{StaticResource Eyebrow}"/>
                  <TextBlock x:Name="SunStatus" Text="Checking" FontSize="20" FontWeight="SemiBold" Foreground="White" Margin="0,8,0,4"/>
                  <TextBlock Text="Screen capture and streaming" Style="{StaticResource Muted}"/>
                </StackPanel>
              </Border>
              <Border Grid.Row="1" Grid.Column="0" Style="{StaticResource Card}" Margin="0,0,7,0">
                <StackPanel>
                  <TextBlock Text="VIRTUAL DISPLAY" Style="{StaticResource Eyebrow}"/>
                  <TextBlock x:Name="DriverStatus" Text="Checking" FontSize="20" FontWeight="SemiBold" Foreground="White" Margin="0,8,0,4"/>
                  <TextBlock Text="Extended desktop adapter" Style="{StaticResource Muted}"/>
                </StackPanel>
              </Border>
              <Border Grid.Row="1" Grid.Column="1" Style="{StaticResource Card}" Margin="7,0,0,0">
                <StackPanel>
                  <TextBlock Text="ANDROID DEVICE" Style="{StaticResource Eyebrow}"/>
                  <TextBlock x:Name="DeviceStatus" Text="Checking" FontSize="20" FontWeight="SemiBold" Foreground="White" Margin="0,8,0,4"/>
                  <TextBlock Text="USB device enrollment" Style="{StaticResource Muted}"/>
                </StackPanel>
              </Border>
            </Grid>
            <Border Style="{StaticResource Card}">
              <Grid>
                <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                <StackPanel>
                  <TextBlock Text="HOW IT WORKS" Style="{StaticResource Eyebrow}"/>
                  <TextBlock Text="A proper extended display, powered by USB." FontSize="16" FontWeight="SemiBold" Foreground="#F5F8FF" Margin="0,8,0,5"/>
                  <TextBlock Text="Windows virtual monitor → Sunshine video → USB bridge → Moonlight on Android." Style="{StaticResource Muted}"/>
                </StackPanel>
                <Button x:Name="OverviewGuide" Grid.Column="1" Content="Connection guide" Style="{StaticResource SecondaryButton}" VerticalAlignment="Center" Margin="10,0,0,0"/>
              </Grid>
            </Border>
          </StackPanel>

          <StackPanel x:Name="ConnectPage" Visibility="Collapsed">
            <Border Style="{StaticResource Card}" Padding="25" Margin="0,0,0,20">
              <StackPanel>
                <TextBlock Text="01  •  PREPARE WINDOWS" Style="{StaticResource Eyebrow}"/>
                <TextBlock Text="Install the pieces only once." Style="{StaticResource Heading}" Margin="0,9,0,7"/>
                <TextBlock Text="OpenTabletDisplay safely reuses compatible Sunshine and virtual-display installations. Setup requires Windows administrator approval and may download verified upstream components." Style="{StaticResource Muted}" Margin="0,0,0,17"/>
                <StackPanel Orientation="Horizontal">
                  <Button x:Name="InstallButton" Style="{StaticResource PrimaryButton}" Content="Install / repair" Margin="0,0,10,0"/>
                  <Button x:Name="OpenLogsButton" Style="{StaticResource SecondaryButton}" Content="See installation log"/>
                </StackPanel>
                <ProgressBar x:Name="InstallProgress" Visibility="Collapsed" IsIndeterminate="True" Height="4" Background="#293A51" Foreground="#5B9BFF" Margin="0,19,0,8"/>
                <TextBlock x:Name="InstallActivity" Text="Waiting to begin." Style="{StaticResource Muted}" Margin="0,12,0,0"/>
              </StackPanel>
            </Border>
            <Border Style="{StaticResource Card}" Margin="0,0,0,18">
              <StackPanel>
                <TextBlock Text="02  •  CONNECT ANDROID" Style="{StaticResource Eyebrow}"/>
                <TextBlock Text="Plug in a USB data cable." Style="{StaticResource Heading}" Margin="0,9,0,7"/>
                <TextBlock Text="Enable Developer options → USB debugging. Unlock Android and approve the Windows computer when prompted. Leave normal USB tethering OFF." Style="{StaticResource Muted}"/>
                <TextBlock Text="You may see one Android VPN/connection permission from gnirehtet. Approve it to carry the stream over USB." Style="{StaticResource Muted}" Margin="0,11,0,0"/>
                <StackPanel Orientation="Horizontal" Margin="0,17,0,0">
                  <Button x:Name="CheckUsbButton" Content="Check connected device" Style="{StaticResource SecondaryButton}"/>
                  <TextBlock x:Name="UsbActivity" Text="Not checked yet" Style="{StaticResource Muted}" Margin="14,10,0,0" VerticalAlignment="Center"/>
                </StackPanel>
                <StackPanel Orientation="Horizontal" Margin="0,11,0,0">
                  <ComboBox x:Name="UsbDeviceList" Width="215" Height="39" Background="#22334A" Foreground="#FFFFFF" BorderBrush="#3A577C" VerticalContentAlignment="Center" Padding="9,5" ToolTip="Authorized Android devices discovered through ADB"/>
                  <Button x:Name="EnrollDeviceButton" Content="Use selected device" Style="{StaticResource SecondaryButton}" Margin="11,0,0,0" IsEnabled="False"/>
                </StackPanel>
              </StackPanel>
            </Border>
            <Border Style="{StaticResource Card}" Margin="0,0,0,18">
              <StackPanel>
                <TextBlock Text="03  •  SET UP MOONLIGHT" Style="{StaticResource Eyebrow}"/>
                <TextBlock Text="Pair with your Windows PC." Style="{StaticResource Heading}" Margin="0,9,0,7"/>
                <TextBlock Text="Install the official Moonlight Game Streaming app on Android. Pair using the host address below and Sunshine's web UI." Style="{StaticResource Muted}" Margin="0,0,0,17"/>
                <Border Background="#0E1928" CornerRadius="10" Padding="13" Margin="0,0,0,13">
                  <Grid>
                    <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                    <StackPanel>
                      <TextBlock Text="MOONLIGHT HOST ADDRESS" Style="{StaticResource Eyebrow}"/>
                      <TextBlock x:Name="HostAddress" Text="Available after USB connection" Foreground="#EAF3FF" FontSize="16" FontWeight="SemiBold" Margin="0,5,0,0"/>
                    </StackPanel>
                    <Button x:Name="CopyHostButton" Content="Copy address" Grid.Column="1" Style="{StaticResource SecondaryButton}" VerticalAlignment="Center" Padding="12,8" MinHeight="35"/>
                  </Grid>
                </Border>
                <StackPanel Orientation="Horizontal">
                  <Button x:Name="MoonlightButton" Content="Get Moonlight" Style="{StaticResource SecondaryButton}" Margin="0,0,10,0"/>
                  <Button x:Name="SunshineButton" Content="Open Sunshine settings" Style="{StaticResource SecondaryButton}"/>
                </StackPanel>
              </StackPanel>
            </Border>
            <Border Style="{StaticResource Card}">
              <StackPanel>
                <TextBlock Text="04  •  USE YOUR DISPLAY" Style="{StaticResource Eyebrow}"/>
                <TextBlock Text="Open Moonlight → Desktop." Style="{StaticResource Heading}" Margin="0,9,0,7"/>
                <TextBlock Text="After first setup, the background agent reconnects when the authorized device is plugged in. No installer window needs to stay open." Style="{StaticResource Muted}"/>
              </StackPanel>
            </Border>
          </StackPanel>

          <StackPanel x:Name="DiagnosticsPage" Visibility="Collapsed">
            <Border Style="{StaticResource Card}" Margin="0,0,0,18">
              <StackPanel>
                <TextBlock Text="LIVE DIAGNOSTICS" Style="{StaticResource Eyebrow}"/>
                <TextBlock Text="Know what is working." Style="{StaticResource Heading}" Margin="0,9,0,7"/>
                <TextBlock Text="Check the running service, virtual display, enrolled device, and USB bridge. Logs stay on your PC unless you explicitly export them." Style="{StaticResource Muted}" Margin="0,0,0,16"/>
                <StackPanel Orientation="Horizontal">
                  <Button x:Name="DoctorButton" Content="Run diagnostics" Style="{StaticResource PrimaryButton}" Margin="0,0,10,0"/>
                  <Button x:Name="ExportButton" Content="Export redacted report" Style="{StaticResource SecondaryButton}"/>
                </StackPanel>
              </StackPanel>
            </Border>
            <Border Style="{StaticResource Card}" Padding="10">
              <StackPanel>
                <Grid Margin="11,6,11,12">
                  <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                  <TextBlock Text="ACTIVITY &amp; DIAGNOSTIC OUTPUT" Style="{StaticResource Eyebrow}" VerticalAlignment="Center"/>
                  <Button x:Name="CopyButton" Grid.Column="1" Content="Copy output" Style="{StaticResource SecondaryButton}" Padding="11,7" MinHeight="31"/>
                </Grid>
                <TextBox x:Name="DiagnosticOutput" Height="355" Background="#0A121E" Foreground="#C8D9EF" BorderThickness="0" Padding="15"
                         IsReadOnly="True" FontFamily="Consolas" FontSize="12" TextWrapping="Wrap" AcceptsReturn="True"
                         VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled"
                         Text="Run diagnostics to inspect your setup.&#10;No system changes are performed by this check."/>
              </StackPanel>
            </Border>
          </StackPanel>

          <StackPanel x:Name="SettingsPage" Visibility="Collapsed">
            <Border Style="{StaticResource Card}" Margin="0,0,0,18">
              <StackPanel>
                <TextBlock Text="EVERYDAY CONTROL" Style="{StaticResource Eyebrow}"/>
                <TextBlock Text="Your display, your choice." Style="{StaticResource Heading}" Margin="0,9,0,7"/>
                <TextBlock Text="The background USB agent handles reconnection after setup. These actions affect only OpenTabletDisplay's own transport and enrollment." Style="{StaticResource Muted}" Margin="0,0,0,17"/>
                <WrapPanel>
                  <Button x:Name="StartBridgeButton" Content="Start / repair USB connection" Style="{StaticResource PrimaryButton}" Margin="0,0,10,10"/>
                  <Button x:Name="StopBridgeButton" Content="Stop USB connection" Style="{StaticResource SecondaryButton}" Margin="0,0,10,10"/>
                  <Button x:Name="ResetDeviceButton" Content="Forget paired Android device" Style="{StaticResource SecondaryButton}" Margin="0,0,10,10"/>
                </WrapPanel>
              </StackPanel>
            </Border>
            <Border Style="{StaticResource Card}" Margin="0,0,0,18">
              <StackPanel>
                <TextBlock Text="SUPPORT &amp; TRANSPARENCY" Style="{StaticResource Eyebrow}"/>
                <TextBlock Text="Useful links. No mystery." Style="{StaticResource Heading}" Margin="0,9,0,7"/>
                <TextBlock Text="OpenTabletDisplay keeps its own files under your Windows user profile. Sunshine and the display driver are separate upstream projects." Style="{StaticResource Muted}" Margin="0,0,0,17"/>
                <WrapPanel>
                  <Button x:Name="OpenFolderButton" Content="Open app folder" Style="{StaticResource SecondaryButton}" Margin="0,0,10,10"/>
                  <Button x:Name="HelpButton" Content="Troubleshooting guide" Style="{StaticResource SecondaryButton}" Margin="0,0,10,10"/>
                  <Button x:Name="ReportButton" Content="Report an issue" Style="{StaticResource SecondaryButton}" Margin="0,0,10,10"/>
                </WrapPanel>
              </StackPanel>
            </Border>
            <Border Background="#1F2B3B" CornerRadius="13" Padding="19">
              <StackPanel>
                <TextBlock Text="SAFE BY DEFAULT" Foreground="#8AB7FC" FontSize="11" FontWeight="Bold"/>
                <TextBlock Text="No remote account. No automatic device switching. No blanket driver deletion." FontSize="14" Foreground="#E6EFFC" Margin="0,9,0,4" TextWrapping="Wrap"/>
                <TextBlock Text="Existing Sunshine settings are preserved where possible; removing dependencies requires a separate explicit operation." Style="{StaticResource Muted}"/>
              </StackPanel>
            </Border>
          </StackPanel>
        </StackPanel>
      </ScrollViewer>
      <Border Grid.Row="1" Background="#101B29" BorderBrush="#26374F" BorderThickness="0,1,0,0" Padding="25,12">
        <Grid>
          <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
          <TextBlock x:Name="FooterText" Text="Checking installation and USB bridge status…" Foreground="#B7C8DF" FontSize="12" VerticalAlignment="Center" TextTrimming="CharacterEllipsis"/>
          <TextBlock Grid.Column="1" Text="LOCAL • PRIVATE • USB-FIRST" Foreground="#728BAC" FontSize="10" FontWeight="Bold" VerticalAlignment="Center"/>
        </Grid>
      </Border>
    </Grid>
  </Grid>
</Window>
'@

try {
    $node = New-Object System.Xml.XmlNodeReader ([xml]$xaml)
    $window = [Windows.Markup.XamlReader]::Load($node)
} catch {
    [System.Windows.MessageBox]::Show(
        ('OpenTabletDisplay could not open its interface. ' + $_.Exception.Message),
        'OpenTabletDisplay', 'OK', 'Error'
    ) | Out-Null
    exit 1
}

$names = @(
    'OverviewPage','ConnectPage','DiagnosticsPage','SettingsPage','NavOverview','NavConnect','NavDiagnostics','NavSettings',
    'RefreshButton','PageEyebrow','PageHeading','PageSubheading','HeroTitle','HeroDescription','HeroSetup','HeroDetails',
    'AppStatus','AppDetail','SunStatus','DriverStatus','DeviceStatus','SidebarDot','SidebarState','OverviewGuide',
    'InstallButton','OpenLogsButton','InstallProgress','InstallActivity','MoonlightButton','SunshineButton','CheckUsbButton','UsbActivity','UsbDeviceList','EnrollDeviceButton','HostAddress','CopyHostButton','DoctorButton',
    'ExportButton','CopyButton','DiagnosticOutput','StartBridgeButton','StopBridgeButton','ResetDeviceButton',
    'OpenFolderButton','HelpButton','ReportButton','FooterText'
)
$script:UI = @{}
foreach ($name in $names) { $script:UI[$name] = $window.FindName($name) }

function Set-Feedback([string]$Message) {
    $script:UI.FooterText.Text = $Message
}
function Show-Message([string]$Message, [string]$Title = 'OpenTabletDisplay') {
    [System.Windows.MessageBox]::Show($window, $Message, $Title, 'OK', 'Information') | Out-Null
}
function Show-Error([string]$Message) {
    [System.Windows.MessageBox]::Show($window, $Message, 'OpenTabletDisplay', 'OK', 'Error') | Out-Null
}
function Open-Link([string]$Url) {
    try { Start-Process -FilePath $Url | Out-Null }
    catch { Show-Error ('Could not open link: ' + $_.Exception.Message) }
}
function Show-Page([string]$Name) {
    $script:CurrentPage = $Name
    $headings = @{
        Overview = @('YOUR WORKSPACE','Overview','A second monitor, without a second monitor.')
        Connect = @('GET STARTED','Set up & connect','Everything you need for a reliable wired display.')
        Diagnostics = @('SYSTEM INSIGHT','Diagnostics','Know exactly what is happening on your PC.')
        Settings = @('PREFERENCES','Controls & help','Take control without disturbing your existing setup.')
    }
    foreach ($page in @('Overview','Connect','Diagnostics','Settings')) {
        $script:UI[($page + 'Page')].Visibility = if ($page -eq $Name) { 'Visible' } else { 'Collapsed' }
        $nav = $script:UI[('Nav' + $page)]
        $nav.Background = if ($page -eq $Name) { '#243A59' } else { 'Transparent' }
        $nav.Foreground = if ($page -eq $Name) { '#FFFFFF' } else { '#9EACBF' }
    }
    $h = $headings[$Name]
    $script:UI.PageEyebrow.Text = $h[0]
    $script:UI.PageHeading.Text = $h[1]
    $script:UI.PageSubheading.Text = $h[2]
    if ($Name -eq 'Diagnostics') { Refresh-DiagnosticText }
}
function Get-LastLogLine([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return '' }
    try {
        $lines = @(Get-Content -LiteralPath $Path -Tail 2 -ErrorAction Stop)
        if ($lines.Count -gt 0) { return [string]$lines[-1] }
    } catch {}
    return ''
}
function Refresh-DiagnosticText {
    $parts = New-Object 'System.Collections.Generic.List[string]'
    $parts.Add('OpenTabletDisplay ' + $script:AppVersion + '  |  Local diagnostics')
    $parts.Add('-----------------------------------------------------')
    $parts.Add('Installation log:')
    if (Test-Path -LiteralPath $script:InstallLog) {
        $parts.Add((Get-Content -LiteralPath $script:InstallLog -Tail 30 | Out-String))
    } else { $parts.Add('No installer log found.') }
    $parts.Add('USB agent log:')
    if (Test-Path -LiteralPath $script:AgentLog) {
        $parts.Add((Get-Content -LiteralPath $script:AgentLog -Tail 25 | Out-String))
    } else { $parts.Add('No agent log found.') }
    if ($script:LastDiagnosticText) {
        $parts.Add('Live doctor output:')
        $parts.Add($script:LastDiagnosticText)
    }
    $script:UI.DiagnosticOutput.Text = ($parts -join [Environment]::NewLine)
}
function Get-Health {
    $statePath = Join-Path $script:InstallRoot 'install-state.json'
    $installed = (Test-Path -LiteralPath $statePath) -and (Test-Path -LiteralPath $script:RuntimeExe)
    $svc = Get-Service -Name 'SunshineService' -ErrorAction SilentlyContinue
    $sunshine = if (-not $svc) { 'Not installed' } elseif ($svc.Status -eq 'Running') { 'Running' } else { [string]$svc.Status }
    $vdd = @(Get-FSDLVddDevices)
    $driver = if ($vdd.Count -eq 0) { 'Not detected' } elseif (@($vdd | Where-Object { $_.Status -eq 'OK' }).Count -gt 0) { 'Detected' } else { 'Needs attention' }
    $sessionPath = Join-Path $script:InstallRoot 'runtime\state.json'
    $hostAddress = ''
    if (Test-Path -LiteralPath $sessionPath) {
        try { $hostAddress = [string](Get-Content -LiteralPath $sessionPath -Raw | ConvertFrom-Json).host_ip } catch {}
    }
    $enrollment = Join-Path $script:InstallRoot 'enrollment.json'
    $device = if (Test-Path -LiteralPath $enrollment) { 'Enrolled' } else { 'Not paired' }
    $task = Get-ScheduledTask -TaskName $script:TaskName -ErrorAction SilentlyContinue
    return [pscustomobject]@{
        Installed = [bool]$installed
        Sunshine = $sunshine
        Driver = $driver
        Device = $device
        Host = $hostAddress
        Agent = if ($task) { [string]$task.State } else { 'Not installed' }
    }
}
function Refresh-Health {
    try {
        $h = Get-Health
        $script:UI.AppStatus.Text = if ($h.Installed) { 'Installed' } else { 'Not installed' }
        $script:UI.AppDetail.Text = if ($h.Installed) { 'Integration and agent files ready' } else { 'Complete the one-time setup' }
        $script:UI.SunStatus.Text = $h.Sunshine
        $script:UI.DriverStatus.Text = $h.Driver
        $script:UI.DeviceStatus.Text = $h.Device
        $script:UI.HostAddress.Text = if ($h.Host) { $h.Host } else { 'Available after USB connection' }
        if ($h.Installed -and $h.Sunshine -eq 'Running' -and $h.Driver -eq 'Detected') {
            $script:UI.HeroTitle.Text = 'Your second screen is ready.'
            $script:UI.HeroDescription.Text = 'Connect an authorized Android device over USB and launch Desktop in Moonlight.'
            $script:UI.SidebarState.Text = 'Windows components ready'
            $script:UI.SidebarDot.Fill = '#57D5A4'
            if (-not $script:Installing) { Set-Feedback ('Sunshine running • virtual display detected • background agent ' + $h.Agent) }
        } elseif ($script:Installing) {
            $script:UI.HeroTitle.Text = 'Preparing your display.'
            $script:UI.HeroDescription.Text = 'Setting up Windows components. The process may request administrator approval.'
            $script:UI.SidebarState.Text = 'Installation in progress'
            $script:UI.SidebarDot.Fill = '#F2BF67'
        } else {
            $script:UI.HeroTitle.Text = if ($h.Installed) { 'Your display needs attention.' } else { 'Make room for more.' }
            $script:UI.HeroDescription.Text = if ($h.Installed) { 'Some components are not healthy. Open setup to repair them without deleting your personal settings.' } else { 'Turn your Android tablet or phone into a real extended Windows monitor over USB.' }
            $script:UI.SidebarState.Text = 'Setup or repair needed'
            $script:UI.SidebarDot.Fill = '#F2BF67'
            if (-not $script:Installing) { Set-Feedback ('Sunshine: ' + $h.Sunshine + ' • virtual display: ' + $h.Driver) }
        }
    } catch {
        Set-Feedback ('Unable to read system health: ' + $_.Exception.Message)
    }
}
function Start-Setup {
    if ($script:Installing) { Show-Message 'Installation is already running.'; return }
    $installer = Get-InstallerScript
    if (-not $installer) {
        Show-Error 'Setup files are missing. Download the packaged OpenTabletDisplay release ZIP, extract it fully, then launch OpenTabletDisplay.exe.'
        return
    }
    try {
        $script:InstallFinished = $false
        $args = '-NoProfile -ExecutionPolicy Bypass -File "' + $installer + '"'
        $script:InstallProcess = Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $args -WindowStyle Hidden -PassThru
        $script:Installing = $true
        $script:UI.InstallProgress.Visibility = 'Visible'
        $script:UI.InstallActivity.Text = 'Administrator approval requested. Preparing verified dependencies…'
        $script:UI.InstallButton.IsEnabled = $false
        Set-Feedback 'Installing components. Do not disconnect your PC or terminate setup.'
        Show-Page 'Connect'
    } catch {
        $script:Installing = $false
        $script:UI.InstallButton.IsEnabled = $true
        $script:UI.InstallProgress.Visibility = 'Collapsed'
        Show-Error ('Setup could not start: ' + $_.Exception.Message)
    }
}
function Poll-Install {
    if (-not $script:Installing) { return }
    $line = Get-LastLogLine $script:InstallLog
    if ($line) { $script:UI.InstallActivity.Text = $line }
    if ($null -ne $script:InstallProcess -and $script:InstallProcess.HasExited) {
        $script:Installing = $false
        $script:UI.InstallProgress.Visibility = 'Collapsed'
        $script:UI.InstallButton.IsEnabled = $true
        $success = $false
        try { $success = ($script:InstallProcess.ExitCode -eq 0) } catch {}
        if ($success -and (Test-Path -LiteralPath (Join-Path $script:InstallRoot 'install-state.json'))) {
            $script:UI.InstallActivity.Text = 'Setup complete. Connect your Android device and open Moonlight.'
            Set-Feedback 'Installation completed successfully.'
            Show-Message 'Windows components are ready. Connect Android using a USB data cable, enable USB debugging, approve the computer, and launch Moonlight.'
        } else {
            $detail = Get-LastLogLine $script:InstallLog
            if (-not $detail) { $detail = 'Installer exited without writing a log.' }
            $script:UI.InstallActivity.Text = $detail
            Set-Feedback 'Setup failed. Open Diagnostics to see the detailed error.'
            Show-Error ('Setup did not complete.' + [Environment]::NewLine + [Environment]::NewLine + $detail + [Environment]::NewLine + [Environment]::NewLine + 'Open Diagnostics for details. Existing user settings will not be deleted automatically.')
        }
        Refresh-Health
    }
}
function Check-USBDevice {
    $adb = Join-Path $script:InstallRoot 'runtime\tools\platform-tools\adb.exe'
    if (-not (Test-Path -LiteralPath $adb)) {
        $script:UI.UsbActivity.Text = 'USB tools missing; run setup first'
        return
    }
    if ($script:UsbProcess -and -not $script:UsbProcess.HasExited) { return }
    try {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $script:UsbOut) | Out-Null
        Remove-Item -LiteralPath $script:UsbOut,$script:UsbErr -Force -ErrorAction SilentlyContinue
        $script:UsbProcess = Start-Process -FilePath $adb -ArgumentList @('devices','-l') -WindowStyle Hidden -PassThru -RedirectStandardOutput $script:UsbOut -RedirectStandardError $script:UsbErr
        $script:UI.UsbActivity.Text = 'Checking Android authorization...'
    } catch { $script:UI.UsbActivity.Text = ('USB check failed: ' + $_.Exception.Message) }
}
function Poll-USBDevice {
    if (-not $script:UsbProcess -or -not $script:UsbProcess.HasExited) { return }
    try {
        $raw = if (Test-Path -LiteralPath $script:UsbOut) { Get-Content -LiteralPath $script:UsbOut -Raw } else { '' }
        $rows = @($raw -split '[\r\n]+' | Where-Object { $_ -match '^\S+\s+(device|unauthorized|offline)\b' })
        $auth = @($rows | Where-Object { $_ -match '^\S+\s+device\b' })
        $unauthed = @($rows | Where-Object { $_ -match '^\S+\s+unauthorized\b' })
        $script:UI.UsbDeviceList.Items.Clear()
        foreach ($item in $auth) {
            if ($item -match '^([A-Za-z0-9._:-]{1,128})\s+device\b') {
                [void]$script:UI.UsbDeviceList.Items.Add([string]$matches[1])
            }
        }
        $script:UI.EnrollDeviceButton.IsEnabled = ($script:UI.UsbDeviceList.Items.Count -gt 0)
        if ($script:UI.UsbDeviceList.Items.Count -eq 1) { $script:UI.UsbDeviceList.SelectedIndex = 0 }
        if ($unauthed.Count -gt 0) { $script:UI.UsbActivity.Text = 'Approve USB debugging on Android' }
        elseif ($auth.Count -eq 1 -and $rows.Count -eq 1) { $script:UI.UsbActivity.Text = 'Authorized Android found' }
        elseif ($auth.Count -gt 1) { $script:UI.UsbActivity.Text = 'Disconnect extra devices' }
        elseif ($rows.Count -gt 0) { $script:UI.UsbActivity.Text = 'Device offline; reconnect USB' }
        else { $script:UI.UsbActivity.Text = 'No Android device found' }
    } catch { $script:UI.UsbActivity.Text = 'USB check could not complete' }
    $script:UsbProcess = $null
}
function Run-Doctor {
    if (-not (Test-Path -LiteralPath $script:RuntimeExe)) {
        Show-Message 'The integration is not installed yet. Run setup first.'
        return
    }
    if ($script:DiagnosticProcess -and -not $script:DiagnosticProcess.HasExited) {
        Show-Message 'A diagnostic check is already running.'
        return
    }
    try {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $script:DiagOut) | Out-Null
        Remove-Item -LiteralPath $script:DiagOut,$script:DiagErr -Force -ErrorAction SilentlyContinue
        $script:DiagnosticProcess = Start-Process -FilePath $script:RuntimeExe -ArgumentList 'doctor' -PassThru -WindowStyle Hidden -RedirectStandardOutput $script:DiagOut -RedirectStandardError $script:DiagErr
        $script:LastDiagnosticText = 'Running live USB, Sunshine and display checks…'
        Refresh-DiagnosticText
        Show-Page 'Diagnostics'
        Set-Feedback 'Diagnostics running locally. No settings are being changed.'
    } catch { Show-Error ('Unable to run diagnostics: ' + $_.Exception.Message) }
}
function Poll-Doctor {
    if (-not $script:DiagnosticProcess) { return }
    if (-not $script:DiagnosticProcess.HasExited) { return }
    try {
        $text = if (Test-Path -LiteralPath $script:DiagOut) { Get-Content -LiteralPath $script:DiagOut -Raw } else { '' }
        $err = if (Test-Path -LiteralPath $script:DiagErr) { Get-Content -LiteralPath $script:DiagErr -Raw } else { '' }
        $script:LastDiagnosticText = ($text + [Environment]::NewLine + $err).Trim()
        if (-not $script:LastDiagnosticText) { $script:LastDiagnosticText = 'Diagnostic process returned no output.' }
        Set-Feedback ('Doctor check completed with exit code ' + $script:DiagnosticProcess.ExitCode + '.')
        Refresh-DiagnosticText
    } catch { Set-Feedback ('Unable to collect diagnostics: ' + $_.Exception.Message) }
    $script:DiagnosticProcess = $null
}
function Invoke-BridgeCommand([string]$Action) {
    if (-not (Test-Path -LiteralPath $script:RuntimeExe)) {
        Show-Message 'Install OpenTabletDisplay before controlling the USB connection.'
        return
    }
    try {
        $out = Join-Path $script:InstallRoot ('logs\manual-' + $Action + '.txt')
        $err = Join-Path $script:InstallRoot ('logs\manual-' + $Action + '-error.txt')
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $out) | Out-Null
        Start-Process -FilePath $script:RuntimeExe -ArgumentList $Action -WindowStyle Hidden -RedirectStandardOutput $out -RedirectStandardError $err | Out-Null
        Set-Feedback ('Requested ' + $Action + '. See Diagnostics for follow-up checks.')
        Show-Message ('Command requested: ' + $Action + '. The background agent may reconnect automatically while your device is authorized.')
    } catch { Show-Error ('Could not run operation: ' + $_.Exception.Message) }
}
function Export-Report {
    Refresh-DiagnosticText
    $save = New-Object Microsoft.Win32.SaveFileDialog
    $save.Title = 'Export redacted OpenTabletDisplay diagnostics'
    $save.FileName = 'OpenTabletDisplay-diagnostics.txt'
    $save.Filter = 'Text report (*.txt)|*.txt'
    if (-not $save.ShowDialog($window)) { return }
    $t = [string]$script:UI.DiagnosticOutput.Text
    # Reports are opt-in; scrub the most common sensitive identifiers.
    if ($env:USERPROFILE) { $t = $t.Replace([string]$env:USERPROFILE, '<USER_PROFILE>') }
    if ($env:USERNAME) { $t = $t.Replace([string]$env:USERNAME, '<USER>') }
    $t = [regex]::Replace($t, '(?im)(Android enrollment:\s*)\S+', '$1<DEVICE_REDACTED>')
    $t = [regex]::Replace($t, '(?im)(serial(?:number)?\s*[:=]\s*)\S+', '$1<DEVICE_REDACTED>')
    $t = [regex]::Replace($t, '(?<!\d)(?:\d{1,3}\.){3}\d{1,3}(?!\d)', '<IP_REDACTED>')
    try {
        [System.IO.File]::WriteAllText($save.FileName, $t)
        Show-Message 'A redacted diagnostic report was saved. Review the contents before sharing.'
    } catch { Show-Error ('Unable to save report: ' + $_.Exception.Message) }
}

$script:UI.NavOverview.Add_Click({ Show-Page 'Overview' })
$script:UI.NavConnect.Add_Click({ Show-Page 'Connect' })
$script:UI.NavDiagnostics.Add_Click({ Show-Page 'Diagnostics' })
$script:UI.NavSettings.Add_Click({ Show-Page 'Settings' })
$script:UI.RefreshButton.Add_Click({ Refresh-Health; if ($script:CurrentPage -eq 'Diagnostics') { Refresh-DiagnosticText } })
$script:UI.HeroSetup.Add_Click({ Show-Page 'Connect' })
$script:UI.HeroDetails.Add_Click({ Show-Page 'Diagnostics' })
$script:UI.OverviewGuide.Add_Click({ Show-Page 'Connect' })
$script:UI.InstallButton.Add_Click({ Start-Setup })
$script:UI.OpenLogsButton.Add_Click({ Show-Page 'Diagnostics'; Refresh-DiagnosticText })
$script:UI.CheckUsbButton.Add_Click({ Check-USBDevice })
$script:UI.EnrollDeviceButton.Add_Click({
    $serial = [string]$script:UI.UsbDeviceList.SelectedItem
    if ($serial -notmatch '^[A-Za-z0-9._:-]{1,128}
$script:UI.CopyHostButton.Add_Click({
    $address = [string]$script:UI.HostAddress.Text
    if ($address -match '^\d{1,3}(\.\d{1,3}){3}
$script:UI.SunshineButton.Add_Click({ Open-Link 'https://localhost:47990' })
$script:UI.DoctorButton.Add_Click({ Run-Doctor })
$script:UI.CopyButton.Add_Click({
    try { [System.Windows.Clipboard]::SetText($script:UI.DiagnosticOutput.Text); Set-Feedback 'Diagnostic text copied. Check for private data before sharing.' }
    catch { Show-Error ('Unable to copy: ' + $_.Exception.Message) }
})
$script:UI.ExportButton.Add_Click({ Export-Report })
$script:UI.StartBridgeButton.Add_Click({ Invoke-BridgeCommand 'start' })
$script:UI.StopBridgeButton.Add_Click({ Invoke-BridgeCommand 'stop' })
$script:UI.ResetDeviceButton.Add_Click({
    $answer = [System.Windows.MessageBox]::Show($window,
        'Forget the currently enrolled Android device? You will need to authorize and enroll a device again. This does not remove Sunshine or Windows drivers.',
        'Forget Android device?', 'YesNo', 'Warning')
    if ($answer -eq 'Yes') { Invoke-BridgeCommand 'reset-device' }
})
$script:UI.OpenFolderButton.Add_Click({
    if (Test-Path -LiteralPath $script:InstallRoot) { Start-Process -FilePath explorer.exe -ArgumentList ('"' + $script:InstallRoot + '"') }
    else { Show-Message 'No installation folder exists yet.' }
})
$script:UI.HelpButton.Add_Click({ Open-Link 'https://github.com/0pen-Sourcer/OpenTabletDisplay/blob/main/docs/TROUBLESHOOTING.md' })
$script:UI.ReportButton.Add_Click({ Open-Link 'https://github.com/0pen-Sourcer/OpenTabletDisplay/issues/new/choose' })

$timer = New-Object System.Windows.Threading.DispatcherTimer
$timer.Interval = [TimeSpan]::FromSeconds(2)
$script:Ticks = 0
$timer.Add_Tick({
    Poll-Install
    Poll-Doctor
    Poll-USBDevice
    $script:Ticks++
    if ($script:Ticks % 3 -eq 0) { Refresh-Health }
})
$window.Add_Loaded({ Refresh-Health; $timer.Start() })
$window.Add_Closing({ $timer.Stop() })
Show-Page 'Overview'
[void]$window.ShowDialog()
) {
        [System.Windows.Clipboard]::SetText($address)
        Set-Feedback 'Moonlight host address copied.'
    } else { Show-Message 'No active host address yet. Connect Android and let the USB bridge start.' }
})
$script:UI.MoonlightButton.Add_Click({ Open-Link 'https://play.google.com/store/apps/details?id=com.limelight' })
$script:UI.SunshineButton.Add_Click({ Open-Link 'https://localhost:47990' })
$script:UI.DoctorButton.Add_Click({ Run-Doctor })
$script:UI.CopyButton.Add_Click({
    try { [System.Windows.Clipboard]::SetText($script:UI.DiagnosticOutput.Text); Set-Feedback 'Diagnostic text copied. Check for private data before sharing.' }
    catch { Show-Error ('Unable to copy: ' + $_.Exception.Message) }
})
$script:UI.ExportButton.Add_Click({ Export-Report })
$script:UI.StartBridgeButton.Add_Click({ Invoke-BridgeCommand 'start' })
$script:UI.StopBridgeButton.Add_Click({ Invoke-BridgeCommand 'stop' })
$script:UI.ResetDeviceButton.Add_Click({
    $answer = [System.Windows.MessageBox]::Show($window,
        'Forget the currently enrolled Android device? You will need to authorize and enroll a device again. This does not remove Sunshine or Windows drivers.',
        'Forget Android device?', 'YesNo', 'Warning')
    if ($answer -eq 'Yes') { Invoke-BridgeCommand 'reset-device' }
})
$script:UI.OpenFolderButton.Add_Click({
    if (Test-Path -LiteralPath $script:InstallRoot) { Start-Process -FilePath explorer.exe -ArgumentList ('"' + $script:InstallRoot + '"') }
    else { Show-Message 'No installation folder exists yet.' }
})
$script:UI.HelpButton.Add_Click({ Open-Link 'https://github.com/0pen-Sourcer/OpenTabletDisplay/blob/main/docs/TROUBLESHOOTING.md' })
$script:UI.ReportButton.Add_Click({ Open-Link 'https://github.com/0pen-Sourcer/OpenTabletDisplay/issues/new/choose' })

$timer = New-Object System.Windows.Threading.DispatcherTimer
$timer.Interval = [TimeSpan]::FromSeconds(2)
$script:Ticks = 0
$timer.Add_Tick({
    Poll-Install
    Poll-Doctor
    $script:Ticks++
    if ($script:Ticks % 3 -eq 0) { Refresh-Health }
})
$window.Add_Loaded({ Refresh-Health; $timer.Start() })
$window.Add_Closing({ $timer.Stop() })
Show-Page 'Overview'
[void]$window.ShowDialog()
) {
        Show-Message 'Choose a detected, authorized Android device first.'
        return
    }
    $answer = [System.Windows.MessageBox]::Show($window,
        ('Use Android device ' + $serial + ' for your second display? This changes the enrolled USB device only.'),
        'Select Android device', 'YesNo', 'Question')
    if ($answer -ne 'Yes') { return }
    try {
        $out = Join-Path $script:InstallRoot 'logs\manual-enroll.txt'
        $err = Join-Path $script:InstallRoot 'logs\manual-enroll-error.txt'
        Start-Process -FilePath $script:RuntimeExe -ArgumentList @('enroll',$serial) -PassThru -WindowStyle Hidden -RedirectStandardOutput $out -RedirectStandardError $err | Out-Null
        Set-Feedback 'Device enrollment requested. Run diagnostics to confirm the result.'
    } catch { Show-Error ('Unable to enroll the device: ' + $_.Exception.Message) }
})
$script:UI.CopyHostButton.Add_Click({
    $address = [string]$script:UI.HostAddress.Text
    if ($address -match '^\d{1,3}(\.\d{1,3}){3}
$script:UI.SunshineButton.Add_Click({ Open-Link 'https://localhost:47990' })
$script:UI.DoctorButton.Add_Click({ Run-Doctor })
$script:UI.CopyButton.Add_Click({
    try { [System.Windows.Clipboard]::SetText($script:UI.DiagnosticOutput.Text); Set-Feedback 'Diagnostic text copied. Check for private data before sharing.' }
    catch { Show-Error ('Unable to copy: ' + $_.Exception.Message) }
})
$script:UI.ExportButton.Add_Click({ Export-Report })
$script:UI.StartBridgeButton.Add_Click({ Invoke-BridgeCommand 'start' })
$script:UI.StopBridgeButton.Add_Click({ Invoke-BridgeCommand 'stop' })
$script:UI.ResetDeviceButton.Add_Click({
    $answer = [System.Windows.MessageBox]::Show($window,
        'Forget the currently enrolled Android device? You will need to authorize and enroll a device again. This does not remove Sunshine or Windows drivers.',
        'Forget Android device?', 'YesNo', 'Warning')
    if ($answer -eq 'Yes') { Invoke-BridgeCommand 'reset-device' }
})
$script:UI.OpenFolderButton.Add_Click({
    if (Test-Path -LiteralPath $script:InstallRoot) { Start-Process -FilePath explorer.exe -ArgumentList ('"' + $script:InstallRoot + '"') }
    else { Show-Message 'No installation folder exists yet.' }
})
$script:UI.HelpButton.Add_Click({ Open-Link 'https://github.com/0pen-Sourcer/OpenTabletDisplay/blob/main/docs/TROUBLESHOOTING.md' })
$script:UI.ReportButton.Add_Click({ Open-Link 'https://github.com/0pen-Sourcer/OpenTabletDisplay/issues/new/choose' })

$timer = New-Object System.Windows.Threading.DispatcherTimer
$timer.Interval = [TimeSpan]::FromSeconds(2)
$script:Ticks = 0
$timer.Add_Tick({
    Poll-Install
    Poll-Doctor
    Poll-USBDevice
    $script:Ticks++
    if ($script:Ticks % 3 -eq 0) { Refresh-Health }
})
$window.Add_Loaded({ Refresh-Health; $timer.Start() })
$window.Add_Closing({ $timer.Stop() })
Show-Page 'Overview'
[void]$window.ShowDialog()
) {
        [System.Windows.Clipboard]::SetText($address)
        Set-Feedback 'Moonlight host address copied.'
    } else { Show-Message 'No active host address yet. Connect Android and let the USB bridge start.' }
})
$script:UI.MoonlightButton.Add_Click({ Open-Link 'https://play.google.com/store/apps/details?id=com.limelight' })
$script:UI.SunshineButton.Add_Click({ Open-Link 'https://localhost:47990' })
$script:UI.DoctorButton.Add_Click({ Run-Doctor })
$script:UI.CopyButton.Add_Click({
    try { [System.Windows.Clipboard]::SetText($script:UI.DiagnosticOutput.Text); Set-Feedback 'Diagnostic text copied. Check for private data before sharing.' }
    catch { Show-Error ('Unable to copy: ' + $_.Exception.Message) }
})
$script:UI.ExportButton.Add_Click({ Export-Report })
$script:UI.StartBridgeButton.Add_Click({ Invoke-BridgeCommand 'start' })
$script:UI.StopBridgeButton.Add_Click({ Invoke-BridgeCommand 'stop' })
$script:UI.ResetDeviceButton.Add_Click({
    $answer = [System.Windows.MessageBox]::Show($window,
        'Forget the currently enrolled Android device? You will need to authorize and enroll a device again. This does not remove Sunshine or Windows drivers.',
        'Forget Android device?', 'YesNo', 'Warning')
    if ($answer -eq 'Yes') { Invoke-BridgeCommand 'reset-device' }
})
$script:UI.OpenFolderButton.Add_Click({
    if (Test-Path -LiteralPath $script:InstallRoot) { Start-Process -FilePath explorer.exe -ArgumentList ('"' + $script:InstallRoot + '"') }
    else { Show-Message 'No installation folder exists yet.' }
})
$script:UI.HelpButton.Add_Click({ Open-Link 'https://github.com/0pen-Sourcer/OpenTabletDisplay/blob/main/docs/TROUBLESHOOTING.md' })
$script:UI.ReportButton.Add_Click({ Open-Link 'https://github.com/0pen-Sourcer/OpenTabletDisplay/issues/new/choose' })

$timer = New-Object System.Windows.Threading.DispatcherTimer
$timer.Interval = [TimeSpan]::FromSeconds(2)
$script:Ticks = 0
$timer.Add_Tick({
    Poll-Install
    Poll-Doctor
    $script:Ticks++
    if ($script:Ticks % 3 -eq 0) { Refresh-Health }
})
$window.Add_Loaded({ Refresh-Health; $timer.Start() })
$window.Add_Closing({ $timer.Stop() })
Show-Page 'Overview'
[void]$window.ShowDialog()
