using System;
using System.Linq;
using Clipper2Lib;
using UnityEngine;

namespace BOM
{
    public sealed class ConnectionProposal
    {
        public PiecePlacement a,b;
        public Vector2 start,end,translationB;
        public float strength = 100;
        public bool attachmentLoss;
        public float minimumFraction;
    }
    public static class ConnectionTools
    {
        public static bool TryPropose(PiecePlacement a,PiecePlacement b,bool previewSnap,out ConnectionProposal proposal,out string error)
        {
            proposal=null;error="Select two distinct approved pieces with a shared nonzero boundary.";
            if(a==null||b==null||a==b||a.piece==null||b.piece==null||!a.piece.IsApproved||!b.piece.IsApproved) return false;
            if(!LevelCompiler.TryPose(a,out var pa,out error)||!LevelCompiler.TryPose(b,out var pb,out error)) return false;
            var ar=Geometry.Transform(a.piece.Draft().profile,pa.ToWorld);var br=Geometry.Transform(b.piece.Draft().profile,pb.ToWorld);
            float best=0;Vector2 start=default,end=default,delta=default;
            foreach(var ea in Geometry.Edges(ar)) foreach(var eb in Geometry.Edges(br))
            {
                var da=(ea.b-ea.a).normalized;var db=(eb.b-eb.a).normalized;
                if(Vector2.Dot(da,db)>-.99999f) continue;
                var move=previewSnap?(ea.a+ea.b-eb.a-eb.b)*.5f:Vector2.zero;
                if(move.magnitude>3) continue;
                var b0=eb.a+move;var b1=eb.b+move;
                if(Math.Abs(Geometry.Cross(da,b0-ea.a))>Geometry.ContactTolerance||Math.Abs(Geometry.Cross(da,b1-ea.a))>Geometry.ContactTolerance) continue;
                float lo=Mathf.Max(0,Mathf.Min(Vector2.Dot(b0-ea.a,da),Vector2.Dot(b1-ea.a,da)));
                float hi=Mathf.Min(ea.Length,Mathf.Max(Vector2.Dot(b0-ea.a,da),Vector2.Dot(b1-ea.a,da)));
                if(hi-lo<=Geometry.ContactTolerance||hi-lo<=best) continue;
                best=hi-lo;start=ea.a+da*lo;end=ea.a+da*hi;delta=move;
            }
            if(best==0) {error="No shared boundary of nonzero length. Move/rotate the pieces, or preview alignment of parallel edges within 3 metres. Point contacts cannot form a bond.";return false;}
            var adjusted=Geometry.Transform(br,p=>p+delta);
            if(!TryValidateSeparation(ar,adjusted,out error)) return false;
            proposal=new ConnectionProposal {a=a,b=b,start=start,end=end,translationB=delta};error=null;return true;
        }
        internal static bool TryValidateSeparation(Region worldA,Region worldB,out string error)
        {
            error=null;
            try
            {
                double overlap=Clipper.Area(Clipper.Intersect(Geometry.Paths(worldA),Geometry.Paths(worldB),FillRule.NonZero))/(Geometry.Units*Geometry.Units);
                if(Math.Abs(overlap)<=Geometry.AreaTolerance) return true;
                error="The pieces overlap in solid area. Move them to share an exterior boundary.";return false;
            }
            catch(Exception e) {error="Connection geometry: "+e.Message+" Correct the current profiles or transforms.";return false;}
        }
        public static bool TryValidateProposal(ConnectionProposal proposal,out string error)
        {
            error="Select two distinct approved pieces and a finite, nonzero boundary extent with valid strength and lifetime inputs.";
            if(proposal?.a==null||proposal.b==null||proposal.a==proposal.b||proposal.a.piece==null||proposal.b.piece==null
                ||!proposal.a.piece.IsApproved||!proposal.b.piece.IsApproved||!Geometry.Finite(proposal.start)||!Geometry.Finite(proposal.end)
                ||!Geometry.Finite(proposal.translationB)||!Geometry.Positive(proposal.strength)||!Geometry.Nonnegative(proposal.minimumFraction)
                ||proposal.minimumFraction>1||Vector2.Distance(proposal.start,proposal.end)<=Geometry.ContactTolerance) return false;
            if(!LevelCompiler.TryPose(proposal.a,out var pa,out error)||!LevelCompiler.TryPose(proposal.b,out var pb,out error)) return false;
            var ar=proposal.a.piece.Draft().profile;var br=proposal.b.piece.Draft().profile;
            if(!Geometry.TryValidate(ar,out error)||!Geometry.TryValidate(br,out error)) return false;
            var a0=(Vector2)proposal.a.transform.InverseTransformPoint(proposal.start);var a1=(Vector2)proposal.a.transform.InverseTransformPoint(proposal.end);
            var b0=(Vector2)proposal.b.transform.InverseTransformPoint(proposal.start-proposal.translationB);
            var b1=(Vector2)proposal.b.transform.InverseTransformPoint(proposal.end-proposal.translationB);
            if(!Geometry.FullBoundary(ar,a0,a1)||!Geometry.FullBoundary(br,b0,b1))
            {error="The edited extent must remain on both current approved boundaries. Re-propose or shorten the highlighted extent.";return false;}
            return TryValidateSeparation(Geometry.Transform(ar,pa.ToWorld),Geometry.Transform(br,p=>pb.ToWorld(p)+proposal.translationB),out error);
        }
        public static bool TrySet(ConnectionAuthoring connection,ConnectionProposal proposal,out string error)
        {
            error="Connection proposal is missing or invalid.";
            if(connection==null||!TryValidateProposal(proposal,out error)) return false;
            if(!LevelCompiler.TryPose(proposal.a,out var pa,out error)||!LevelCompiler.TryPose(proposal.b,out var pb,out error)) return false;
            if(proposal.translationB.sqrMagnitude>Geometry.Tolerance*Geometry.Tolerance) {error="Apply the confirmed alignment transform before saving the paired regions.";return false;}
            var a0=(Vector2)proposal.a.transform.InverseTransformPoint(proposal.start);var a1=(Vector2)proposal.a.transform.InverseTransformPoint(proposal.end);
            var b0=(Vector2)proposal.b.transform.InverseTransformPoint(proposal.start);var b1=(Vector2)proposal.b.transform.InverseTransformPoint(proposal.end);
            var fit=new BondState {a0=pa.Bake(a0),a1=pa.Bake(a1),b0=pb.Bake(b0),b1=pb.Bake(b1)};fit.EstablishFit();
            connection.a=proposal.a;connection.b=proposal.b;connection.a0=a0;connection.a1=a1;connection.b0=b0;connection.b1=b1;
            connection.strength=proposal.strength;connection.attachmentLoss=proposal.attachmentLoss;connection.minimumFraction=proposal.minimumFraction;
            connection.referenceLength=Vector2.Distance(proposal.start,proposal.end);
            connection.restTranslation=fit.restTranslation;connection.restAngle=fit.restAngle;error=null;return true;
        }
    }
}
