#include "Internal.hxx"
#include <HLRBRep_Algo.hxx>
#include <HLRBRep_HLRToShape.hxx>
#include <HLRAlgo_Projector.hxx>
#include <BRepAdaptor_Curve.hxx>
#include <TopExp_Explorer.hxx>
#include <TopoDS.hxx>
#include <gp_Ax2.hxx>
#include <gp_Dir.hxx>
#include <Standard_Failure.hxx>
#include <cmath>
namespace DrawVerify::Occt { namespace { void add(const TopoDS_Shape&s,bool hidden,ProjectionResult&r){for(TopExp_Explorer e(s,TopAbs_EDGE);e.More();e.Next()){BRepAdaptor_Curve c(TopoDS::Edge(e.Current()));auto a=c.Value(c.FirstParameter()),b=c.Value(c.LastParameter());if(c.GetType()==GeomAbs_Line){Line2 x{{a.X(),a.Y()},{b.X(),b.Y()},hidden};(hidden?r.hiddenLines:r.visibleLines).push_back(x);}else if(c.GetType()==GeomAbs_Circle){auto q=c.Circle();Arc2 x{{q.Location().X(),q.Location().Y()},q.Radius(),c.FirstParameter(),c.LastParameter(),hidden};(hidden?r.hiddenArcs:r.visibleArcs).push_back(x);}else{Polyline2 x;x.hidden=hidden;for(int i=0;i<=16;i++){auto q=c.Value(c.FirstParameter()+(c.LastParameter()-c.FirstParameter())*i/16);x.points.push_back({q.X(),q.Y()});}(hidden?r.hiddenPolylines:r.visiblePolylines).push_back(std::move(x));}}}gp_Dir direction(ProjectionView v){switch(v){case ProjectionView::Front:return gp_Dir(0,-1,0);case ProjectionView::Rear:return gp_Dir(0,1,0);case ProjectionView::Top:return gp_Dir(0,0,1);case ProjectionView::Bottom:return gp_Dir(0,0,-1);case ProjectionView::Left:return gp_Dir(-1,0,0);case ProjectionView::Right:return gp_Dir(1,0,0);default:throw std::invalid_argument("Custom projection requires direction vectors");}} }
ProjectionResult Project(const std::shared_ptr<Model::Impl>&i,ProjectionView view,const Vector3*d,const Vector3*u){if(!i||i->shape.IsNull())throw std::runtime_error("Projection requires a loaded model");try{gp_Dir d0=view==ProjectionView::Custom?gp_Dir(d->x,d->y,d->z):direction(view);gp_Dir u0=view==ProjectionView::Custom?gp_Dir(u->x,u->y,u->z):(std::abs(d0.Z())>.999999?gp_Dir(0,1,0):gp_Dir(0,0,1));if(std::abs(d0.Dot(u0))>.999999)throw std::invalid_argument("Projection direction and up direction must not be parallel");opencascade::handle<HLRBRep_Algo> a=new HLRBRep_Algo;a->Add(i->shape);a->Projector(HLRAlgo_Projector(gp_Ax2(gp_Pnt(0,0,0),d0,u0)));a->Update();a->Hide();HLRBRep_HLRToShape out(a);ProjectionResult r;add(out.VCompound(),false,r);add(out.HCompound(),true,r);return r;}catch(const Standard_Failure&e){throw std::runtime_error(e.what());}}}
