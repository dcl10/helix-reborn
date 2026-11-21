import { Database, FlaskConical, LineChart, Workflow } from "lucide-react";
import { redirect } from "next/navigation";
import { Suspense } from "react";

import { auth } from "@/auth";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { signInWithKeycloak } from "@/lib/auth/actions";
import { isSignedIn } from "@/lib/auth/authorized";

const features = [
  { icon: Database, text: "Explore your data" },
  { icon: Workflow, text: "Build training pipelines" },
  { icon: LineChart, text: "Evaluate and reuse models (ONNX)" },
];

export default function HomePage() {
  return (
    <div className="flex min-h-svh items-center justify-center p-4">
      <Suspense fallback={<Hero disabled />}>
        <HeroGate />
      </Suspense>
    </div>
  );
}

/** Reads the session and redirects signed-in users to `/experiments`; reads cookies, so it sits behind `<Suspense>`. */
async function HeroGate() {
  const session = await auth();
  if (isSignedIn(session)) redirect("/experiments");

  return <Hero />;
}

function Hero({ disabled = false }: { disabled?: boolean }) {
  const signIn = signInWithKeycloak.bind(null, undefined);

  return (
    <Card className="w-full max-w-md">
      <CardHeader className="items-center text-center">
        <FlaskConical className="size-10" />
        <CardTitle className="text-2xl">Helix</CardTitle>
        <CardDescription>
          Build, train and evaluate machine learning models without writing
          code.
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        <ul className="flex flex-col gap-2 text-sm text-muted-foreground">
          {features.map(({ icon: Icon, text }) => (
            <li key={text} className="flex items-center gap-2">
              <Icon className="size-4" />
              {text}
            </li>
          ))}
        </ul>
        <form action={signIn}>
          <Button type="submit" className="w-full" disabled={disabled}>
            Sign in
          </Button>
        </form>
        <p className="text-center text-xs text-muted-foreground">
          Access is by invitation only.
        </p>
      </CardContent>
    </Card>
  );
}
