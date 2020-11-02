#ifndef __ISFORWARDPASS_H__
#define __ISFORWARDPASS_H__

void IsForwardPass_float(out bool Out)
{
#if defined(SHADERPASS) && (SHADERPASS == SHADERPASS_FORWARD)
	Out = true;
#else
	Out = false;
#endif
}

#endif//__ISFORWARDPASS_H__