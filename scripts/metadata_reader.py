import struct, sys, json
from pathlib import Path

TABLE_SCHEMAS = {
0:[('u2',None),('str',None),('guid',None),('guid',None),('guid',None)],
1:[('coded','ResolutionScope'),('str',None),('str',None)],
2:[('u4',None),('str',None),('str',None),('coded','TypeDefOrRef'),('table',4),('table',6)],
3:[('table',4)],
4:[('u2',None),('str',None),('blob',None)],
5:[('table',6)],
6:[('u4',None),('u2',None),('u2',None),('str',None),('blob',None),('table',8)],
7:[('table',8)],
8:[('u2',None),('u2',None),('str',None)],
9:[('table',2),('coded','TypeDefOrRef')],
10:[('coded','MemberRefParent'),('str',None),('blob',None)],
11:[('u1',None),('u1',None),('coded','HasConstant'),('blob',None)],
12:[('coded','HasCustomAttribute'),('coded','CustomAttributeType'),('blob',None)],
13:[('coded','HasFieldMarshal'),('blob',None)],
14:[('u2',None),('coded','HasDeclSecurity'),('blob',None)],
15:[('u2',None),('u4',None),('table',2)],
16:[('u4',None),('table',4)],
17:[('blob',None)],
18:[('table',2),('table',20)],
19:[('table',20)],
20:[('u2',None),('str',None),('coded','TypeDefOrRef')],
21:[('table',2),('table',23)],
22:[('table',23)],
23:[('u2',None),('str',None),('blob',None)],
24:[('u2',None),('table',6),('coded','HasSemantics')],
25:[('table',2),('coded','MethodDefOrRef'),('coded','MethodDefOrRef')],
26:[('str',None)],
27:[('blob',None)],
28:[('u2',None),('coded','MemberForwarded'),('str',None),('table',26)],
29:[('u4',None),('table',4)],
30:[('u4',None),('u4',None)],
31:[('u4',None)],
32:[('u4',None),('u2',None),('u2',None),('u2',None),('u2',None),('u4',None),('blob',None),('str',None),('str',None)],
33:[('u4',None)],
34:[('u4',None),('u4',None),('u4',None)],
35:[('u2',None),('u2',None),('u2',None),('u2',None),('u4',None),('blob',None),('str',None),('str',None),('blob',None)],
36:[('u4',None),('table',35)],
37:[('u4',None),('u4',None),('u4',None),('table',35)],
38:[('u4',None),('str',None),('blob',None)],
39:[('u4',None),('u4',None),('str',None),('str',None),('coded','Implementation')],
40:[('u4',None),('u4',None),('str',None),('coded','Implementation')],
41:[('table',2),('table',2)],
42:[('u2',None),('u2',None),('coded','TypeOrMethodDef'),('str',None)],
43:[('coded','MethodDefOrRef'),('blob',None)],
44:[('table',42),('coded','TypeDefOrRef')],
}
CODED = {
'TypeDefOrRef':(2,[2,1,27]),
'HasConstant':(2,[4,8,23]),
'HasCustomAttribute':(5,[6,4,1,2,8,9,10,0,23,20,17,26,27,32,35,38,39,40,42,44,43]),
'HasFieldMarshal':(1,[4,8]),
'HasDeclSecurity':(2,[2,6,32]),
'MemberRefParent':(3,[2,1,26,6,27]),
'HasSemantics':(1,[20,23]),
'MethodDefOrRef':(1,[6,10]),
'MemberForwarded':(1,[4,6]),
'Implementation':(2,[38,35,39]),
'CustomAttributeType':(3,[None,None,6,10,None]),
'ResolutionScope':(2,[0,26,35,1]),
'TypeOrMethodDef':(1,[2,6]),
}
ET = {0x01:'void',0x02:'bool',0x03:'char',0x04:'sbyte',0x05:'byte',0x06:'short',0x07:'ushort',0x08:'int',0x09:'uint',0x0a:'long',0x0b:'ulong',0x0c:'float',0x0d:'double',0x0e:'string',0x16:'typedref',0x18:'nint',0x19:'nuint',0x1c:'object'}

class CLR:
 def __init__(self,path):
  self.path=Path(path); self.data=self.path.read_bytes(); self._parse_pe(); self._parse_meta(); self._parse_tables(); self._build_names()
 def u16(self,o): return struct.unpack_from('<H',self.data,o)[0]
 def u32(self,o): return struct.unpack_from('<I',self.data,o)[0]
 def _parse_pe(self):
  pe=self.u32(0x3c); nsec=self.u16(pe+6); opt=pe+24; magic=self.u16(opt); dd=opt+(112 if magic==0x20b else 96); cli_rva=self.u32(dd+14*8); sec=opt+self.u16(pe+20); self.sections=[]
  for i in range(nsec):
   o=sec+i*40; name=self.data[o:o+8].rstrip(b'\0').decode(errors='ignore'); vs,va,rawsz,raw=struct.unpack_from('<IIII',self.data,o+8); self.sections.append((va,max(vs,rawsz),raw,name))
  self.cli_off=self.rva(cli_rva); self.meta_rva=self.u32(self.cli_off+8); self.meta_off=self.rva(self.meta_rva)
 def rva(self,r):
  for va,sz,raw,n in self.sections:
   if va<=r<va+sz:return raw+(r-va)
  raise ValueError(f'RVA {r:x}')
 def _parse_meta(self):
  o=self.meta_off; assert self.data[o:o+4]==b'BSJB'; verlen=self.u32(o+12); o=o+16+verlen; o=(o+3)&~3; flags,streams=struct.unpack_from('<HH',self.data,o);o+=4; self.streams={}
  for _ in range(streams):
   off,size=struct.unpack_from('<II',self.data,o);o+=8; end=self.data.index(b'\0',o); name=self.data[o:end].decode(); o=(end+4)&~3; self.streams[name]=(self.meta_off+off,size)
  self.str_off=self.streams.get('#Strings',(0,0))[0]; self.blob_off=self.streams.get('#Blob',(0,0))[0]; self.guid_off=self.streams.get('#GUID',(0,0))[0]
 def _idx_size(self,table): return 2 if self.rows.get(table,0)<65536 else 4
 def _coded_size(self,name):
  bits,tables=CODED[name]; mx=max([self.rows.get(t,0) for t in tables if t is not None] or [0]); return 2 if mx < (1<<(16-bits)) else 4
 def _heap_size(self,k):
  return {'str':4 if self.heap_sizes&1 else 2,'guid':4 if self.heap_sizes&2 else 2,'blob':4 if self.heap_sizes&4 else 2}[k]
 def _col_size(self,c):
  k,a=c
  if k=='u1':return 1
  if k=='u2':return 2
  if k=='u4':return 4
  if k in ('str','guid','blob'):return self._heap_size(k)
  if k=='table':return self._idx_size(a)
  if k=='coded':return self._coded_size(a)
  raise KeyError(c)
 def _parse_tables(self):
  off,_=self.streams.get('#~',self.streams.get('#-'))
  o=off+4; major,minor,self.heap_sizes,res=struct.unpack_from('<BBBB',self.data,o);o+=4; valid=struct.unpack_from('<Q',self.data,o)[0];o+=8; sortedm=struct.unpack_from('<Q',self.data,o)[0];o+=8
  self.rows={}
  for t in range(64):
   if valid>>t&1:self.rows[t]=self.u32(o);o+=4
  self.table_off={};self.row_size={}
  cur=o
  for t in range(64):
   if valid>>t&1:
    schema=TABLE_SCHEMAS.get(t)
    if schema is None:raise NotImplementedError(f'table {t}')
    rs=sum(self._col_size(c) for c in schema);self.table_off[t]=cur;self.row_size[t]=rs;cur+=rs*self.rows[t]
 def _read_idx(self,o,size):return (self.u16(o) if size==2 else self.u32(o)),o+size
 def row(self,t,rid):
  if rid<1 or rid>self.rows.get(t,0):return None
  o=self.table_off[t]+(rid-1)*self.row_size[t]; vals=[]
  for k,a in TABLE_SCHEMAS[t]:
   if k=='u1':v=self.data[o];o+=1
   elif k=='u2':v=self.u16(o);o+=2
   elif k=='u4':v=self.u32(o);o+=4
   elif k in ('str','guid','blob'):v,o=self._read_idx(o,self._heap_size(k))
   elif k=='table':v,o=self._read_idx(o,self._idx_size(a))
   elif k=='coded':v,o=self._read_idx(o,self._coded_size(a))
   vals.append(v)
  return vals
 def s(self,i):
  if not i:return ''
  o=self.str_off+i; e=self.data.index(b'\0',o);return self.data[o:e].decode('utf-8','replace')
 def comp(self,b,p=0):
  x=b[p]
  if x&0x80==0:return x,p+1
  if x&0xC0==0x80:return ((x&0x3f)<<8)|b[p+1],p+2
  return ((x&0x1f)<<24)|(b[p+1]<<16)|(b[p+2]<<8)|b[p+3],p+4
 def blob(self,i):
  if not i:return b''
  o=self.blob_off+i; ln,p=self.comp(self.data,o); return self.data[p:p+ln]
 def decode_coded(self,name,v):
  bits,tabs=CODED[name];tag=v&((1<<bits)-1);rid=v>>bits;return (tabs[tag] if tag<len(tabs) else None,rid)
 def _build_names(self):
  self.typeref={};self.typedef={};self.nested={}
  for i in range(1,self.rows.get(1,0)+1):
   r=self.row(1,i); ns,n=self.s(r[2]),self.s(r[1]);self.typeref[i]=(ns+'.' if ns else '')+n
  for i in range(1,self.rows.get(2,0)+1):
   r=self.row(2,i); ns,n=self.s(r[2]),self.s(r[1]);self.typedef[i]=(ns+'.' if ns else '')+n
  for i in range(1,self.rows.get(41,0)+1):
   r=self.row(41,i);self.nested[r[0]]=r[1]
  def full(i):
   if i not in self.nested:return self.typedef[i]
   outer=full(self.nested[i]);name=self.typedef[i].split('.')[-1];return outer+'+'+name
  for i in list(self.typedef):self.typedef[i]=full(i)
  self.type_methods={};self.method_owner={};self.type_fields={};self.field_owner={}
  cnt=self.rows.get(2,0)
  for i in range(1,cnt+1):
   r=self.row(2,i); nr=self.row(2,i+1) if i<cnt else None
   fs,ms=r[4],r[5];fe=(nr[4] if nr else self.rows.get(4,0)+1);me=(nr[5] if nr else self.rows.get(6,0)+1)
   self.type_fields[i]=list(range(fs,fe));self.type_methods[i]=list(range(ms,me))
   for x in range(fs,fe):self.field_owner[x]=i
   for x in range(ms,me):self.method_owner[x]=i
 def type_token_name(self,enc):
  tag=enc&3;rid=enc>>2
  return self.typedef.get(rid,'TypeDef#'+str(rid)) if tag==0 else self.typeref.get(rid,'TypeRef#'+str(rid)) if tag==1 else self.type_spec(rid) if tag==2 else '?'
 def parse_type(self,b,p):
  mods=[]
  while p<len(b) and b[p] in (0x1f,0x20):
   kind=b[p];tok,p=self.comp(b,p+1);mods.append(('modreq' if kind==0x1f else 'modopt')+'('+self.type_token_name(tok)+')')
  if p>=len(b):return '?',p
  e=b[p];p+=1
  if e in ET:return ET[e],p
  if e==0x0f:
   t,p=self.parse_type(b,p);return t+'*',p
  if e==0x10:
   t,p=self.parse_type(b,p);return 'ref '+t,p
  if e in (0x11,0x12):
   tok,p=self.comp(b,p);return self.type_token_name(tok),p
  if e==0x13:
   n,p=self.comp(b,p);return '!'+str(n),p
  if e==0x1e:
   n,p=self.comp(b,p);return '!!'+str(n),p
  if e==0x1d:
   t,p=self.parse_type(b,p);return t+'[]',p
  if e==0x14:
   t,p=self.parse_type(b,p);rank,p=self.comp(b,p);ns,p=self.comp(b,p);sizes=[]
   for _ in range(ns):x,p=self.comp(b,p);sizes.append(x)
   nl,p=self.comp(b,p);lbs=[]
   for _ in range(nl):x,p=self.comp(b,p);lbs.append(x)
   return t+'['+','*(max(rank,1)-1)+']',p
  if e==0x15:
   kind=b[p];p+=1;tok,p=self.comp(b,p);base=self.type_token_name(tok);n,p=self.comp(b,p);args=[]
   for _ in range(n):x,p=self.parse_type(b,p);args.append(x)
   return base+'<'+', '.join(args)+'>',p
  if e==0x45:
   t,p=self.parse_type(b,p);return 'pinned '+t,p
  if e==0x41:return '...',p
  if e==0x1b:return 'fnptr',p
  return f'ET{e:02x}',p
 def method_sig(self,blob):
  if not blob:return '?'
  p=0;cc=blob[p];p+=1;gen=None
  if cc&0x10:gen,p=self.comp(blob,p)
  n,p=self.comp(blob,p);ret,p=self.parse_type(blob,p);pars=[]
  for _ in range(n):t,p=self.parse_type(blob,p);pars.append(t)
  return {'cc':cc,'generic':gen,'ret':ret,'params':pars}
 def field_sig(self,blob):
  if not blob:return '?'
  p=1 if blob and blob[0]==6 else 0;t,p=self.parse_type(blob,p);return t
 def property_sig(self,blob):
  return self.method_sig(blob)
 def type_spec(self,rid):
  r=self.row(27,rid)
  if not r:return 'TypeSpec#'+str(rid)
  b=self.blob(r[0]);t,_=self.parse_type(b,0);return t
 def find_types(self,needle):return [(i,n) for i,n in self.typedef.items() if needle.lower() in n.lower()]
 def dump_type(self,name):
  matches=[(i,n) for i,n in self.typedef.items() if n==name or n.endswith('.'+name) or n.endswith('+'+name)]
  out=[]
  for i,n in matches:
   r=self.row(2,i);ext=self.decode_coded('TypeDefOrRef',r[3]); extname=self.typedef.get(ext[1]) if ext[0]==2 else self.typeref.get(ext[1]) if ext[0]==1 else None
   ent={'rid':i,'name':n,'flags':r[0],'extends':extname,'fields':[],'methods':[]}
   for f in self.type_fields[i]:
    fr=self.row(4,f);ent['fields'].append({'name':self.s(fr[1]),'flags':fr[0],'type':self.field_sig(self.blob(fr[2]))})
   for m in self.type_methods[i]:
    mr=self.row(6,m);sig=self.method_sig(self.blob(mr[4]));
    # param names
    next_m=self.row(6,m+1) if m<self.rows.get(6,0) else None; ps=mr[5];pe=next_m[5] if next_m else self.rows.get(8,0)+1; names={}
    for pr in range(ps,pe):
     rr=self.row(8,pr);names[rr[1]]=self.s(rr[2])
    params=[]
    for k,t in enumerate(sig['params'],1):params.append({'name':names.get(k,''),'type':t})
    ent['methods'].append({'rid':m,'name':self.s(mr[3]),'flags':mr[2],'implflags':mr[1],'rva':mr[0],'ret':sig['ret'],'params':params,'cc':sig['cc']})
   out.append(ent)
  return out

if __name__=='__main__':
 c=CLR(sys.argv[1])
 if len(sys.argv)==2:
  print(json.dumps({'types':len(c.typedef),'methods':c.rows.get(6,0),'assemblyrefs':c.rows.get(35,0)},indent=2))
 elif sys.argv[2]=='find':
  print(json.dumps(c.find_types(sys.argv[3]),indent=2))
 elif sys.argv[2]=='type':
  print(json.dumps(c.dump_type(sys.argv[3]),indent=2))
