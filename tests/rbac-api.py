"""Integration checks against a local API. Uses isolated temporary organizations; cleans them up.
JWTs are generated in memory from local configuration; never printed or written.
Run: python tests/rbac-api.py (API must run on http://127.0.0.1:5089).
"""
import base64, hashlib, hmac, json, subprocess, time, urllib.request, urllib.error, uuid
from pathlib import Path

config = json.loads(Path("appsettings.json").read_text(encoding="utf-8-sig"))
server = "HAROON-SHARIF-P\\SQLEXPRESS04"
marker = "rbac-test-" + uuid.uuid4().hex

def sql(query):
    result = subprocess.run(["sqlcmd", "-S", server, "-E", "-C", "-b", "-d", "AiEmailer", "-y", "0", "-Q", "SET NOCOUNT ON; " + query], capture_output=True, text=True)
    if result.returncode: raise RuntimeError(result.stdout + result.stderr)
    return result.stdout.strip()

def token(user, org, uuqid, register=True):
    session=str(uuid.uuid4())
    if register: sql(f"INSERT dbo.skey_login_session(id_session,id_user,id_org,expires_at) VALUES('{session}',{user},{org},DATEADD(MINUTE,10,SYSUTCDATETIME()));")
    def enc(value): return base64.urlsafe_b64encode(json.dumps(value,separators=(",",":")).encode()).rstrip(b"=")
    header=enc({"alg":"HS256","typ":"JWT"})
    payload=enc({"jti":session,"sub":str(user),"org_id":str(org),"uuqid":uuqid,"iss":config["Jwt"]["Issuer"],"aud":config["Jwt"]["Audience"],"exp":int(time.time())+600})
    body=header+b"."+payload
    signature=base64.urlsafe_b64encode(hmac.new(config["Jwt"]["Key"].encode(),body,hashlib.sha256).digest()).rstrip(b"=")
    return (body+b"."+signature).decode()

def request(jwt, path, method="GET", body=None):
    headers={"X-System-Key":config["SystemKey"]["Key"],"Content-Type":"application/json"}
    if jwt: headers["Authorization"]="Bearer "+jwt
    req=urllib.request.Request("http://127.0.0.1:5089"+path, headers=headers,method=method,data=None if body is None else json.dumps(body).encode())
    try:
        response=urllib.request.urlopen(req,timeout=20)
    except urllib.error.HTTPError as error: response=error
    raw=response.read()
    try: result=json.loads(raw) if raw else None
    except json.JSONDecodeError: result={"unexpected_response":response.status}
    return response.status,result,response.headers

def check(name, actual, expected):
    assert actual==expected, (name,actual,expected)
    print("PASS "+name)

try:
    data=json.loads(sql(f"""
    DECLARE @org INT,@other INT,@adminRole INT,@memberRole INT,@otherRole INT,@admin INT,@member INT;
    INSERT dbo.skey_orginazations(org_name,ouuid) VALUES('{marker}',NEWID()); SET @org=SCOPE_IDENTITY();
    INSERT dbo.skey_orginazations(org_name,ouuid) VALUES('{marker}-other',NEWID()); SET @other=SCOPE_IDENTITY();
    INSERT dbo.skey_role(role_name,id_org,is_org_admin) VALUES('Test administrator',@org,1); SET @adminRole=SCOPE_IDENTITY();
    INSERT dbo.skey_role(role_name,id_org) VALUES('Test member',@org); SET @memberRole=SCOPE_IDENTITY();
    INSERT dbo.skey_role(role_name,id_org) VALUES('Other organization role',@other); SET @otherRole=SCOPE_IDENTITY();
    INSERT dbo.skey_user(username,fullname,uuqid,id_role,id_org,is_active,failed_attempts) VALUES('{marker}-admin@example.invalid','Test administrator',NEWID(),@adminRole,@org,1,0); SET @admin=SCOPE_IDENTITY();
    INSERT dbo.skey_user(username,fullname,uuqid,id_role,id_org,is_active,failed_attempts) VALUES('{marker}-member@example.invalid','Test member',NEWID(),@memberRole,@org,1,0); SET @member=SCOPE_IDENTITY();
    INSERT dbo.skey_role_menu_permission(id_org,id_role,id_menu,permission,can_view,can_add,can_update,can_delete,createdby,createddt)
    SELECT @org,@adminRole,id_menu,0,1,1,1,1,@admin,GETDATE() FROM dbo.skey_menu;
    SELECT @org org,@other otherOrg,@adminRole adminRole,@memberRole memberRole,@otherRole otherRole,
        @admin admin,@member member,(SELECT CONVERT(VARCHAR(36),uuqid) FROM dbo.skey_user WHERE id_user=@admin) adminUuid,
        (SELECT CONVERT(VARCHAR(36),uuqid) FROM dbo.skey_user WHERE id_user=@member) memberUuid FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;
    """))
    admin=token(data["admin"],data["org"],data["adminUuid"])
    member=token(data["member"],data["org"],data["memberUuid"])
    status,access,headers=request(admin,"/api/permission/me")
    check("Administrator permission JSON",status,200)
    check("Administrator has all four grants for all 20 menus", all(all(grants.values()) for grants in access["menus"].values()) and len(access["menus"])==20,True)
    check("Permission response is not cached",headers.get("Cache-Control"),"no-store")
    check("Unauthenticated access denied",request(None,"/api/permission/me")[0],401)
    check("No-grant member cannot read attachments",request(member,"/api/attachment/list")[0],403)
    check("No-grant member cannot call generation directly",request(member,"/api/reply/generate","POST",{"originalEmailBody":"Test email", "tone":"Professional"})[0],403)
    check("Member cannot manage grants",request(member,"/api/permission/save","POST",{"idRole":data["memberRole"],"permissionVersion":1,"permissions":[]})[0],403)
    check("Cross-organization permission read denied",request(admin,f"/api/permission/list?idRole={data['otherRole']}")[0],404)
    check("Wrong organization token denied",request(token(data["admin"],data["otherOrg"],data["adminUuid"]),"/api/permission/me")[0],401)
    _,menus,_=request(admin,"/api/menu/list")
    attach=next(m["idMenu"] for m in menus if m["menuKey"]=="attachments")
    profile=next(m["idMenu"] for m in menus if m["menuKey"]=="settings.profile")
    grants=[{"idMenu":attach,"view":True,"add":False,"update":False,"delete":False}, {"idMenu":profile,"view":True,"add":False,"update":True,"delete":False}]
    body={"idRole":data["memberRole"],"permissionVersion":1,"permissions":grants}
    check("Administrator saves independent grants",request(admin,"/api/permission/save","POST",body)[0],200)
    check("Existing member token immediately sees new access",request(member,"/api/attachment/list")[0],200)
    check("Member sees only own organization attachments",request(member,"/api/attachment/list")[1],[])
    check("Cross-organization attachment preview denied",request(admin,"/api/attachment/file/1")[0],404)
    check("View does not imply delete",request(member,"/api/attachment/1","DELETE")[0],403)
    check("Stale permission editor rejected",request(admin,"/api/permission/save","POST",body)[0],409)
    check("Profile update preserves role",request(member,"/api/user/me","PUT",{"username":marker+"-member@example.invalid","fullName":"Updated test member","idRole":data["adminRole"]})[0],200)
    check("Profile cannot escalate role",request(member,"/api/permission/me")[1]["roleId"],data["memberRole"])
    check("Cross-organization role assignment denied",request(admin,"/api/user/addupd","POST",{"idUser":data["member"],"username":marker+"-member@example.invalid","fullName":"Test member","idRole":data["otherRole"],"isActive":True})[0],403)
    check("Last administrator access cannot be removed",request(admin,"/api/permission/save","POST",{"idRole":data["adminRole"],"permissionVersion":1,"permissions":[]})[0],400)
    check("Permission revoke succeeds",request(admin,"/api/permission/save","POST",{"idRole":data["memberRole"],"permissionVersion":2,"permissions":[]})[0],200)
    check("Same token is immediately denied after revocation",request(member,"/api/attachment/list")[0],403)
    sql(f"UPDATE dbo.skey_user SET id_role={data['adminRole']} WHERE id_user={data['member']};")
    check("Existing token sees current role assignment",request(member,"/api/permission/me")[1]["roleId"],data["adminRole"])
    sql(f"UPDATE dbo.skey_user SET is_active=0 WHERE id_user={data['member']};")
    check("Inactive account cannot fetch permissions",request(member,"/api/permission/me")[0],401)
    check("Legacy JWT without registered session denied",request(token(data["admin"],data["org"],data["adminUuid"],False),"/api/permission/me")[0],401)
    def handoff(expired=False):
        import secrets
        code=secrets.token_urlsafe(32); verifier=secrets.token_urlsafe(32)
        digest=hashlib.sha256(code.encode()).hexdigest()
        challenge=base64.urlsafe_b64encode(hashlib.sha256(verifier.encode()).digest()).rstrip(b"=").decode()
        seconds=-1 if expired else 60
        sql(f"INSERT dbo.skey_oauth_handoff(code_hash,challenge,id_user,id_org,expires_at) VALUES(0x{digest},'{challenge}',{data['admin']},{data['org']},DATEADD(SECOND,{seconds},SYSUTCDATETIME()));")
        return {"code":code,"verifier":verifier}
    body=handoff()
    check("Wrong browser verifier rejected",request(None,"/api/auth/exchange","POST",{**body,"verifier":"A"*43})[0],400)
    status,result,headers=request(None,"/api/auth/exchange","POST",body)
    check("OAuth code exchanged for JWT response body",status,200)
    oauthToken=result["token"]
    check("OAuth exchange response is not cached", "no-store" in headers.get("Cache-Control",""),True)
    check("OAuth exchange returns actual role",result["idRole"],data["adminRole"])
    check("Consumed OAuth code cannot be replayed",request(None,"/api/auth/exchange","POST",body)[0],400)
    check("Expired OAuth code rejected",request(None,"/api/auth/exchange","POST",handoff(True))[0],400)
    check("Altered OAuth code rejected",request(None,"/api/auth/exchange","POST",{**body,"code":"B"*43})[0],400)
    from concurrent.futures import ThreadPoolExecutor
    racing=handoff()
    with ThreadPoolExecutor(max_workers=2) as pool:
        statuses=list(pool.map(lambda _:request(None,"/api/auth/exchange","POST",racing)[0], range(2)))
    check("Concurrent code exchange succeeds exactly once",sorted(statuses),[200,400])
    check("Logout revokes current session",request(oauthToken,"/api/auth/logout","POST")[0],200)
    check("Logged-out JWT is unusable",request(oauthToken,"/api/permission/me")[0],401)
    check("Other login remains active",request(admin,"/api/permission/me")[0],200)
    check("Repeated logout cannot reuse revoked token",request(oauthToken,"/api/auth/logout","POST")[0],401)
    check("Unsupported external provider rejected",request(None,"/api/auth/login-external?provider=Unknown&challenge="+"A"*43)[0],400)
    check("Missing external browser challenge rejected",request(None,"/api/auth/login-external?provider=Google")[0],400)

finally:
    sql(f"""BEGIN TRANSACTION;
    DELETE s FROM dbo.skey_login_session s JOIN dbo.skey_orginazations o ON o.id_organization=s.id_org WHERE o.org_name IN ('{marker}','{marker}-other');
    DELETE h FROM dbo.skey_oauth_handoff h JOIN dbo.skey_orginazations o ON o.id_organization=h.id_org WHERE o.org_name IN ('{marker}','{marker}-other');
    DELETE a FROM dbo.skey_permission_audit a JOIN dbo.skey_orginazations o ON o.id_organization=a.id_org WHERE o.org_name IN ('{marker}','{marker}-other');
    DELETE p FROM dbo.skey_role_menu_permission p JOIN dbo.skey_orginazations o ON o.id_organization=p.id_org WHERE o.org_name IN ('{marker}','{marker}-other');
    DELETE u FROM dbo.skey_user u JOIN dbo.skey_orginazations o ON o.id_organization=u.id_org WHERE o.org_name IN ('{marker}','{marker}-other');
    DELETE r FROM dbo.skey_role r JOIN dbo.skey_orginazations o ON o.id_organization=r.id_org WHERE o.org_name IN ('{marker}','{marker}-other');
    DELETE dbo.skey_orginazations WHERE org_name IN ('{marker}','{marker}-other'); COMMIT TRANSACTION;""")
    print("Temporary test organizations removed")

