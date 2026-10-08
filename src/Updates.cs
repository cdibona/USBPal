using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace USBPal {
    internal sealed class ReleaseAsset {
        public Version Version; public string Url,Digest,Name; public long Size;
    }
    internal sealed class ReleaseUpdater {
        public const string Api="https://api.github.com/repos/cdibona/USBPal/";
        public static readonly Version Current=Assembly.GetExecutingAssembly().GetName().Version;
        readonly Action<string> status;
        public ReleaseUpdater(Action<string> statusCallback) { status=statusCallback; }
        internal static ReleaseAsset Parse(string json) {
            var release=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(json);
            if(release==null || (bool)release["draft"] || (bool)release["prerelease"]) return null;
            string tag=(string)release["tag_name"]; Version version;
            if(!Regex.IsMatch(tag,@"^v?\d+\.\d+\.\d+(\.\d+)?$") || !Version.TryParse(tag.TrimStart('v'),out version)) return null;
            version=new Version(version.Major,version.Minor,Math.Max(0,version.Build),Math.Max(0,version.Revision));
            string name="USBPal-Setup-"+version.ToString(3)+"-win-x64.exe";
            foreach(var item in (System.Collections.IEnumerable)release["assets"]) {
                var asset=(Dictionary<string,object>)item;
                if((string)asset["name"]!=name) continue;
                string digest=asset.ContainsKey("digest")?asset["digest"] as string:null;
                string url=(string)asset["url"]; Uri parsed;
                if(digest==null || !Regex.IsMatch(digest,@"^sha256:[a-fA-F0-9]{64}$") || !Uri.TryCreate(url,UriKind.Absolute,out parsed) || !IsApiAsset(parsed)) throw new InvalidDataException("Release installer has no valid GitHub SHA-256 digest or asset URL.");
                return new ReleaseAsset { Version=version,Name=name,Url=url,Digest=digest.Substring(7),Size=Convert.ToInt64(asset["size"]) };
            }
            throw new InvalidDataException("This release does not contain the Windows x64 installer.");
        }
        static bool IsApiAsset(Uri uri) { return uri.Scheme=="https" && uri.Host=="api.github.com" && uri.IsDefaultPort && Regex.IsMatch(uri.AbsolutePath,@"^/repos/cdibona/USBPal/releases/assets/\d+$"); }
        internal static bool ValidDownload(Uri uri) { return IsApiAsset(uri) || uri.Scheme=="https" && uri.IsDefaultPort && (uri.Host=="release-assets.githubusercontent.com" || uri.Host=="objects.githubusercontent.com"); }
        internal static bool Verify(string file,string digest,long size) {
            if(new FileInfo(file).Length!=size) return false;
            using(var sha=SHA256.Create()) using(var stream=File.OpenRead(file)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").Equals(digest,StringComparison.OrdinalIgnoreCase);
        }
        public async Task<string> Check(bool verifyPublicRelease=false) {
            status("Checking GitHub releases...");
            try {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using(var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(3)))
                using(var handler=new HttpClientHandler { AllowAutoRedirect=false }) using(var client=new HttpClient(handler) { Timeout=TimeSpan.FromMinutes(3) }) {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("USBPal/"+Current.ToString(3));
                    string json;
                    var latest=await Latest(client);
                    using(var response=latest) {
                        if(response.StatusCode==HttpStatusCode.NotFound) { status("No public release is available yet. Recording continues."); return null; }
                        if(response.StatusCode==HttpStatusCode.Unauthorized || response.StatusCode==HttpStatusCode.Forbidden) { status("GitHub is unavailable or rate limited. Try again later; recording continues."); return null; }
                        response.EnsureSuccessStatusCode(); json=await response.Content.ReadAsStringAsync();
                    }
                    var asset=Parse(json); if(asset==null || asset.Version<=(verifyPublicRelease?new Version(0,0,0,0):Current)) { status("v"+Current.ToString(3)+" - up to date with GitHub releases"); return null; }
                    if(asset.Size<=0 || asset.Size>200*1024*1024) throw new InvalidDataException("Installer size is outside the supported limit.");
                    string dir=Path.Combine(Preferences.Root,"Updates",asset.Version.ToString(3)); Directory.CreateDirectory(dir);
                    string destination=Path.Combine(dir,asset.Name),partial=destination+".download";
                    status("Downloading USBPal "+asset.Version.ToString(3)+"...");
                    try {
                        var uri=new Uri(asset.Url); bool downloaded=false;
                        for(int redirects=0;redirects<5;redirects++) {
                            if(!ValidDownload(uri)) throw new InvalidDataException("Release download redirected to an unsupported host.");
                            using(var request=Request(uri,true)) using(var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token)) {
                                if((int)response.StatusCode>=300 && (int)response.StatusCode<400 && response.Headers.Location!=null) { uri=new Uri(uri,response.Headers.Location); continue; }
                                response.EnsureSuccessStatusCode();
                                using(var source=await response.Content.ReadAsStreamAsync()) using(var target=File.Create(partial)) {
                                    var buffer=new byte[65536]; long total=0; int read;
                                    while((read=await source.ReadAsync(buffer,0,buffer.Length,deadline.Token))>0) { total+=read; if(total>asset.Size) throw new InvalidDataException("Installer exceeds expected size."); await target.WriteAsync(buffer,0,read,deadline.Token); }
                                }
                                downloaded=true; break;
                            }
                        }
                        if(!downloaded || !Verify(partial,asset.Digest,asset.Size)) throw new InvalidDataException("Installer checksum verification failed.");
                        if(File.Exists(destination)) File.Delete(destination); File.Move(partial,destination);
                        status("Update "+asset.Version.ToString(3)+" verified - installs when you minimize or close to tray"); return destination;
                    } finally { if(File.Exists(partial)) File.Delete(partial); }
                }
            } catch(Exception ex) { status("Update check failed (recording continues): "+ex.Message); return null; }
        }
        static async Task<HttpResponseMessage> Latest(HttpClient client) {
            using(var request=Request(new Uri(Api+"releases/latest"),false)) return await client.SendAsync(request);
        }
        static HttpRequestMessage Request(Uri uri,bool binary) {
            var request=new HttpRequestMessage(HttpMethod.Get,uri);
            request.Headers.Accept.ParseAdd(binary?"application/octet-stream":"application/vnd.github+json");
            return request;
        }
    }
}

