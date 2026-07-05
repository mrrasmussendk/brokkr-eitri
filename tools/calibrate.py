#!/usr/bin/env python3
"""Calibrate Eitri's TokenEstimator against tiktoken o200k_base on a C# tree.
Usage: python3 tools/calibrate.py <dir-with-cs-files>"""
import sys, os, tiktoken
enc = tiktoken.get_encoding("o200k_base")
def est(text):
    t=i=0; n=len(text)
    while i<n:
        c=text[i]
        if c.isspace(): i+=1; continue
        if c.isalpha() or c=='_':
            s=i
            while i<n and (text[i].isalnum() or text[i]=='_'): i+=1
            t+=max(1,(i-s+5)//6); continue
        if c.isdigit():
            s=i
            while i<n and text[i].isdigit(): i+=1
            t+=max(1,(i-s+2)//3); continue
        s=i
        while i<n and not text[i].isalnum() and not text[i].isspace() and text[i]!='_' and i-s<3: i+=1
        t+=1
    return t
te=tr=0
for root,_,fs in os.walk(sys.argv[1]):
    for f in fs:
        if f.endswith(".cs"):
            s=open(os.path.join(root,f)).read(); te+=est(s); tr+=len(enc.encode(s))
print(f"estimator {te:,} vs o200k {tr:,} -> {(te/tr-1)*100:+.1f}%")
